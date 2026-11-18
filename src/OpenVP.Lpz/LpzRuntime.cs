using OpenVP.Core;

namespace OpenVP.Lpz;

/// <summary>Loads compiled SIMPL Windows programs (.lpz).</summary>
public sealed class LpzLoader : IProgramLoader
{
    public bool CanLoad(string path) => LpzInspect.IsLpz(path);

    public IProgramRuntime Load(string path, ProgramSlot slot)
    {
        var pkg = LpzPackage.Open(path);
        // A .sig next to the program comes from the same compile; the one inside the source archive
        // can be from the compile before (the archive is written first), so it is checked before use.
        var sigPath = Path.ChangeExtension(path, ".sig");
        if (File.Exists(sigPath)) pkg.UseSignals(SigTable.Parse(File.ReadAllBytes(sigPath)));
        else if (pkg.Signals != null && !SigCheck.Matches(pkg.Logic, pkg.Signals, out var agreement))
        {
            slot.Log("Warning", $"Signal names inside the archive don't match this compile ({agreement:P0} agree); showing signal numbers. Upload the .sig from the compile folder to get names.");
            pkg.UseSignals(null);
        }
        return new LpzRuntime(slot, pkg);
    }
}

/// <summary>
/// Runs the compiled logic of a SIMPL Windows program. Each record becomes a node; when a signal
/// changes, every node reading it runs, and its outputs drive further signals until the logic settles.
/// Everything happens on the slot thread, one change at a time.
/// </summary>
public sealed partial class LpzRuntime : IProgramRuntime
{
    private const int MaxStepsPerSolve = 500_000;

    private readonly ProgramSlot _slot;
    private readonly LpzPackage _pkg;
    // Values live in one typed array per kind, indexed by signal number; _kinds[sig] says which one is
    // in use (the other two stay at their defaults for that signal). Nothing here boxes.
    private readonly bool[] _dig;
    private readonly ushort[] _ana;
    private readonly string[] _ser;
    private readonly SigKind[] _kinds;
    private readonly bool[] _kindKnown;
    // Listeners per signal, in the order the nodes were registered. Built as lists while the program
    // loads, then flattened: signal s's listeners are _edges[_edgeStart[s] .. _edgeStart[s + 1]).
    private List<(Node node, int pin)>[]? _fanout;
    private Edge[] _edges = Array.Empty<Edge>();
    private int[] _edgeStart = Array.Empty<int>();
    private readonly bool[] _used;
    private readonly List<Node> _nodes = new();
    private readonly List<DeviceNode> _devices = new();
    private readonly Queue<Pending> _pending = new();
    private readonly Queue<Action> _nextWave = new();
    private readonly TimerSet _timers = new();
    private readonly NvramStore _store;
    private bool _solving;
    private volatile bool _running;

    public LpzRuntime(ProgramSlot slot, LpzPackage pkg)
    {
        _slot = slot;
        _pkg = pkg;
        _store = new NvramStore(Path.Combine(slot.Directory, "nvram.json"));
        var count = Math.Max(2, pkg.Logic.MaxSignal + 1);
        _dig = new bool[count];
        _ana = new ushort[count];
        _ser = new string[count];
        _kinds = new SigKind[count];
        _kindKnown = new bool[count];
        _fanout = new List<(Node, int)>[count];
        _used = new bool[count];

        // Signal types: from the .sig when we have it, then from what the primitives imply.
        if (pkg.Signals != null)
            foreach (var e in pkg.Signals.Entries.Values)
                if (e.Index is > 1 && e.Index < count && e.Kind is SigFileKind.Digital or SigFileKind.Analog or SigFileKind.Serial) // 0 and 1 are the constants
                {
                    _kinds[e.Index] = (SigKind)(int)e.Kind;
                    _kindKnown[e.Index] = true;
                }

        Build();
        Array.Fill(_ser, string.Empty);
        _dig[1] = true; // constant "1"
        Flatten();
    }

    /// <summary>One listener of a signal: the node and which of its pins reads it (negative = its own output).</summary>
    private readonly record struct Edge(Node Node, int Pin);

    /// <summary>A queued change. Digital and analog keep their value in Num, serial in Str.</summary>
    private readonly record struct Pending(int Sig, uint Num, string? Str, string Source);

    private void Flatten()
    {
        var lists = _fanout!;
        _edgeStart = new int[lists.Length + 1];
        var total = 0;
        for (int s = 0; s < lists.Length; s++)
        {
            _edgeStart[s] = total;
            total += lists[s]?.Count ?? 0;
        }
        _edgeStart[lists.Length] = total;
        _edges = new Edge[total];
        for (int s = 0; s < lists.Length; s++)
        {
            if (lists[s] is not { } list) continue;
            for (int i = 0; i < list.Count; i++) _edges[_edgeStart[s] + i] = new Edge(list[i].node, list[i].pin);
        }
        _fanout = null; // only needed while building
    }

    private LpzLoadContext? _alc;
    private readonly List<SplusNode> _modules = new();

    internal ProgramSlot Slot => _slot;

    public string Kind => "SIMPL Windows (compiled)";
    public string ProgramName => _pkg.ProgramName;
    public LpzPackage Package => _pkg;

    /// <summary>Record types present in the program that OpenVP can't run yet, with counts.</summary>
    public Dictionary<int, int> Unsupported { get; } = new();

    public int RunnableSymbols => _nodes.Count;

    // ------------------------------------------------------------------ build

    private void Build()
    {
        foreach (var sym in _pkg.Logic.Symbols)
        {
            if (sym.IsDevice)
            {
                if (sym.IpId is int ipid && sym.Type == -12)
                {
                    var dev = new DeviceNode(this, sym, (uint)ipid);
                    _devices.Add(dev);
                    _devicesByPath[sym.Path] = dev;
                    Register(dev);
                }
                else if (BuildSpecial(sym)) { }
                else if (sym.Type == -48)
                {
                    var node = CreateModule(sym);
                    if (node != null) { _modules.Add(node); Register(node); }
                    else Count(sym.Type);
                }
                continue;
            }
            if (sym.Type == 31) continue; // compile header
            if (BuildSpecial(sym)) continue;
            var prim = PrimitiveLibrary.Create(sym.Type);
            if (prim == null) { Count(sym.Type); continue; }
            Register(new LogicNode(this, sym, prim));
        }

        void Count(int t) => Unsupported[t] = Unsupported.GetValueOrDefault(t) + 1;
    }

    private readonly HashSet<string> _moduleErrors = new();

    /// <summary>Load and instantiate the SIMPL+ module a −48 record points at.</summary>
    private SplusNode? CreateModule(BinSymbol sym)
    {
        var dll = sym.Params.Count > 1 ? sym.Params[1].Text : null;
        if (string.IsNullOrEmpty(dll)) return null;
        try
        {
            if (_alc == null)
            {
                var dir = Path.Combine(_slot.Directory, "lpz");
                _pkg.ExtractAssemblies(dir);
                _alc = new LpzLoadContext(_slot.Number, dir, AppContext.BaseDirectory);
                // Point the SIMPL# runtime used by modules and their libraries at this slot.
                var shim = _alc.LoadFromAssemblyName(new System.Reflection.AssemblyName("OpenVP.SimplSharp"));
                shim.GetType("OpenVP.SimplSharp.OpenVPBridge")?.GetProperty("Slot")?.SetValue(null, _slot);
            }
            var asm = _alc.LoadFromAssemblyName(new System.Reflection.AssemblyName(Path.GetFileNameWithoutExtension(dll)));
            var splusObject = _alc.LoadFromAssemblyName(new System.Reflection.AssemblyName("SplusObjects"))
                .GetType("Crestron.Logos.SplusObjects.SplusObject", throwOnError: true)!;
            var type = asm.GetTypes().FirstOrDefault(t => !t.IsAbstract && splusObject.IsAssignableFrom(t))
                ?? throw new InvalidDataException("no SIMPL+ module class");
            var ctor = type.GetConstructors().First(c => c.GetParameters().Length == 3);
            var encType = ctor.GetParameters()[2].ParameterType;
            var instance = ctor.Invoke(new object[] { sym.Path, sym.Path, Enum.ToObject(encType, 1) });
            return new SplusNode(this, sym, (ISplusModule)instance);
        }
        catch (Exception e)
        {
            var inner = e is System.Reflection.TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
            var unresolved = _alc?.Unresolved.Count > 0 ? $" (missing: {string.Join(", ", _alc.Unresolved.Distinct())})" : "";
            if (_moduleErrors.Add(dll!))
                _slot.Log("Warning", $"SIMPL+ module {dll} can't run yet: {inner.GetType().Name}: {inner.Message}{unresolved}");
            return null;
        }
    }

    private void Register(Node node)
    {
        _nodes.Add(node);
        for (int pin = 1; pin <= node.Symbol.Inputs.Length; pin++)
        {
            var s = node.Symbol.Inputs[pin - 1];
            if (s <= 1) continue;
            _used[s] = true;
            Infer(s, node.InputKind(pin));
            (_fanout![s] ??= new()).Add((node, pin));
        }
        for (int pin = 1; pin <= node.Symbol.Outputs.Length; pin++)
        {
            var s = node.Symbol.Outputs[pin - 1];
            if (s <= 1) continue;
            _used[s] = true;
            Infer(s, node.OutputKind(pin));
            if (node.WatchesOutputs) (_fanout![s] ??= new()).Add((node, -pin)); // negative pin = own output
        }
    }

    private void Infer(int sig, SigKind? kind)
    {
        if (kind is not SigKind k || _kindKnown[sig]) return;
        _kinds[sig] = k;
        _kindKnown[sig] = true;
    }

    // ------------------------------------------------------------------ signals

    /// <summary>The value as an object, for devices, SIMPL+ and the debugger. Boxes analog values; the hot path
    /// uses the typed readers instead.</summary>
    internal object Value(int sig)
    {
        if ((uint)sig >= (uint)_kinds.Length) return SigValue.False;
        return _kinds[sig] switch
        {
            SigKind.Digital => SigValue.Box(_dig[sig]),
            SigKind.Analog => _ana[sig],
            _ => _ser[sig]
        };
    }

    // A signal only ever writes the array of its own kind, so reading another kind's array gives that
    // kind's default (false, 0, ""): same answers as the old "is true / is ushort / as string" checks.
    internal bool DigitalAt(int sig) => (uint)sig < (uint)_dig.Length && _dig[sig];
    internal ushort AnalogAt(int sig) => (uint)sig < (uint)_ana.Length ? _ana[sig] : (ushort)0;
    internal string SerialAt(int sig) => (uint)sig < (uint)_ser.Length ? _ser[sig] : string.Empty;

    internal SigKind KindOf(int sig) => sig >= 0 && sig < _kinds.Length ? _kinds[sig] : SigKind.Digital;

    internal string NameOf(int sig) =>
        _pkg.Signals?.NameOf(sig) is string n ? (n.StartsWith("::") ? n[2..] : n) : $"#{sig}";

    // The Drive overloads queue a change and solve until settled (slot thread only). Each one converts
    // the value to the signal's kind exactly as SigValue.Coerce does, without boxing.

    internal void Drive(int sig, bool value, string source)
    {
        if (sig <= 1 || sig >= _kinds.Length) return; // constants and unconnected
        // An unconnected input reads the constant signal 0/1; into a serial signal that means "empty".
        if (_kinds[sig] == SigKind.Serial) Enqueue(sig, 0, string.Empty, source);
        else Enqueue(sig, value ? 1u : 0u, null, source);
        Solve();
    }

    internal void Drive(int sig, ushort value, string source)
    {
        if (sig <= 1 || sig >= _kinds.Length) return;
        switch (_kinds[sig])
        {
            case SigKind.Digital: Enqueue(sig, value != 0 ? 1u : 0u, null, source); break;
            case SigKind.Analog: Enqueue(sig, value, null, source); break;
            default: Enqueue(sig, 0, value.ToString(), source); break;
        }
        Solve();
    }

    internal void Drive(int sig, string? value, string source)
    {
        if (sig <= 1 || sig >= _kinds.Length) return;
        switch (_kinds[sig])
        {
            case SigKind.Digital:
                Enqueue(sig, value != null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)) ? 1u : 0u, null, source);
                break;
            case SigKind.Analog: Enqueue(sig, ushort.TryParse(value, out var u) ? u : (ushort)0, null, source); break;
            default: Enqueue(sig, 0, value ?? string.Empty, source); break;
        }
        Solve();
    }

    /// <summary>For values that arrive as objects (panels, SIMPL+, the debugger): the three known types take the typed path.</summary>
    internal void Drive(int sig, object value, string source)
    {
        switch (value)
        {
            case bool b: Drive(sig, b, source); return;
            case ushort u: Drive(sig, u, source); return;
            case string s: Drive(sig, s, source); return;
        }
        if (sig <= 1 || sig >= _kinds.Length) return;
        var kind = _kinds[sig];
        var v = SigValue.Coerce(kind, value); // other numeric types, null
        switch (kind)
        {
            case SigKind.Digital: Enqueue(sig, (bool)v ? 1u : 0u, null, source); break;
            case SigKind.Analog: Enqueue(sig, (ushort)v, null, source); break;
            default: Enqueue(sig, 0, (string)v, source); break;
        }
        Solve();
    }

    private void Enqueue(int sig, uint num, string? str, string source) => _pending.Enqueue(new Pending(sig, num, str, source));

    private void Solve()
    {
        if (_solving) return;
        _solving = true;
        try
        {
            var steps = 0;
            while (_pending.Count > 0 || _nextWave.Count > 0)
            {
                if (_pending.Count == 0)
                {
                    // Logic settled: run what was deferred to the next wave.
                    var n = _nextWave.Count;
                    for (int i = 0; i < n; i++) _nextWave.Dequeue()();
                    continue;
                }
                if (++steps > MaxStepsPerSolve)
                {
                    _pending.Clear();
                    _slot.Log("Error", "Logic did not settle (oscillating loop?). Stopped after 500,000 steps.");
                    break;
                }
                var change = _pending.Dequeue();
                var s = change.Sig;
                int first = _edgeStart[s], end = _edgeStart[s + 1];
                switch (_kinds[s])
                {
                    case SigKind.Digital:
                    {
                        var v = change.Num != 0;
                        var old = _dig[s];
                        if (old == v) continue;
                        _dig[s] = v;
                        if (_slot.TraceWanted) _slot.Trace(NameOf(s), SigKind.Digital, SigValue.Box(v), change.Source);
                        for (int e = first; e < end; e++)
                        {
                            var edge = _edges[e];
                            try { edge.Node.OnDigital(edge.Pin, old, v); }
                            catch (Exception ex) { _slot.Log("Error", $"{edge.Node.Symbol}: {ex.Message}"); }
                        }
                        break;
                    }
                    case SigKind.Analog:
                    {
                        var v = (ushort)change.Num;
                        var old = _ana[s];
                        if (old == v) continue;
                        _ana[s] = v;
                        if (_slot.TraceWanted) _slot.Trace(NameOf(s), SigKind.Analog, v, change.Source);
                        for (int e = first; e < end; e++)
                        {
                            var edge = _edges[e];
                            try { edge.Node.OnAnalog(edge.Pin, old, v); }
                            catch (Exception ex) { _slot.Log("Error", $"{edge.Node.Symbol}: {ex.Message}"); }
                        }
                        break;
                    }
                    default:
                    {
                        // Serials always propagate, even when equal.
                        var v = change.Str ?? string.Empty;
                        var old = _ser[s];
                        _ser[s] = v;
                        if (_slot.TraceWanted) _slot.Trace(NameOf(s), SigKind.Serial, v, change.Source);
                        for (int e = first; e < end; e++)
                        {
                            var edge = _edges[e];
                            try { edge.Node.OnSerial(edge.Pin, old, v); }
                            catch (Exception ex) { _slot.Log("Error", $"{edge.Node.Symbol}: {ex.Message}"); }
                        }
                        break;
                    }
                }
            }
        }
        finally { _solving = false; }
    }

    internal void Defer(Action a)
    {
        _nextWave.Enqueue(a);
        if (!_solving) _slot.Dispatcher.Post(static (rt, _, _) => ((LpzRuntime)rt!).Solve(), this, null, 0);
    }

    internal IDisposable After(long ms, Action action)
    {
        return _timers.After(ms, () => _slot.Dispatcher.Post(static (rt, a, _) => { if (((LpzRuntime)rt!)._running) ((Action)a!)(); }, this, action, 0));
    }

    // ------------------------------------------------------------------ lifecycle

    public void Start()
    {
        _slot.Dispatcher.Invoke(() =>
        {
            _running = true;
            foreach (var d in _devices) d.Attach();
            _solving = true; // collect start-up changes, then solve once
            try
            {
                foreach (var n in _nodes)
                {
                    try { n.Start(); }
                    catch (Exception e) { _slot.Log("Error", $"{n.Symbol}: {e.Message}"); }
                }
                // Constant "1" is high from the start: give its listeners their rising edge.
                for (int e = _edgeStart[1]; e < _edgeStart[2]; e++) _edges[e].Node.OnDigital(_edges[e].Pin, false, true);
            }
            finally { _solving = false; }
            Solve();
        });

        var total = _pkg.Logic.Symbols.Count;
        _slot.Log("Notice", $"Compiled program: {_nodes.Count - _devices.Count - _modules.Count} logic symbols, {_modules.Count} SIMPL+ modules and {_devices.Count} devices running" +
                             (_pkg.Signals == null ? " (no .sig: signals shown by number)" : ""));
        if (Unsupported.Count > 0)
            _slot.Log("Warning", $"Not supported yet: " + string.Join(", ",
                Unsupported.OrderByDescending(p => p.Value).Select(p => $"type {p.Key}{(p.Key == -48 ? " (SIMPL+)" : "")} ×{p.Value}")));
    }

    public void Stop()
    {
        _running = false;
        foreach (var m in _modules) m.Stop();
        foreach (var n in _nodes)
            if (n is not SplusNode)
                try { n.Stop(); } catch (Exception) { }
        _store.Flush();
        _timers.CancelAll();
        foreach (var d in _devices) d.Detach();
    }

    public IReadOnlyList<SignalInfo> GetSignals()
    {
        var list = new List<SignalInfo>();
        if (_pkg.Signals != null)
        {
            foreach (var e in _pkg.Signals.Entries.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                if (e.Index > 1 && e.Index < _kinds.Length && !e.Name.StartsWith("::"))
                    list.Add(new SignalInfo(e.Name, _kinds[e.Index], Value(e.Index)));
        }
        else
        {
            for (int i = 2; i < _kinds.Length; i++)
                if (_used[i]) list.Add(new SignalInfo($"#{i}", _kinds[i], Value(i)));
        }
        return list;
    }

    public bool TrySetSignal(string name, object value)
    {
        int sig = -1;
        if (name.StartsWith('#') && int.TryParse(name[1..], out var n)) sig = n;
        else if (_pkg.Signals != null)
            sig = _pkg.Signals.Entries.Values.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Index ?? -1;
        if (sig <= 1 || sig >= _kinds.Length) return false;
        _slot.Dispatcher.Post(() => Drive(sig, value, "debugger"));
        return true;
    }

    public void Dispose()
    {
        Stop();
        _alc?.Unload();
        _alc = null;
    }

    // ------------------------------------------------------------------ nodes

    internal abstract class Node(LpzRuntime rt, BinSymbol sym)
    {
        public LpzRuntime Runtime { get; } = rt;
        public BinSymbol Symbol { get; } = sym;
        public abstract SigKind? InputKind(int pin);
        public abstract SigKind? OutputKind(int pin);
        public virtual bool WatchesOutputs => false;
        public virtual void Start() { }
        public virtual void Stop() { }
        /// <summary>A change on one of this node's input pins, boxed. Nodes that don't need the typed
        /// versions below (devices, SIMPL+, sockets) implement only this.</summary>
        public abstract void OnInput(int pin, object oldValue, object newValue);
        // Typed notifications, so logic nodes read the values without boxing. By default they box and
        // call OnInput (booleans use the shared boxes, so only analog values allocate).
        public virtual void OnDigital(int pin, bool oldValue, bool newValue) => OnInput(pin, SigValue.Box(oldValue), SigValue.Box(newValue));
        public virtual void OnAnalog(int pin, ushort oldValue, ushort newValue) => OnInput(pin, oldValue, newValue);
        public virtual void OnSerial(int pin, string oldValue, string newValue) => OnInput(pin, oldValue, newValue);
    }

    private sealed class LogicNode(LpzRuntime rt, BinSymbol sym, Primitive prim) : Node(rt, sym), IPrimitiveContext
    {
        public override SigKind? InputKind(int pin) => prim.InputKind(pin);
        public override SigKind? OutputKind(int pin) => prim.OutputKind(pin);
        public override bool WatchesOutputs => prim is IWatchesOutputs;
        public override void Start() => prim.Start(this);
        public override void OnInput(int pin, object o, object n)
        {
            if (pin < 0) ((IWatchesOutputs)prim).OnOutputChanged(this, -pin, n);
            else prim.OnInput(this, pin, o, n);
        }
        public override void OnDigital(int pin, bool o, bool n)
        {
            if (pin < 0) ((IWatchesOutputs)prim).OnOutputChanged(this, -pin, SigValue.Box(n));
            else prim.OnDigital(this, pin, o, n);
        }
        public override void OnAnalog(int pin, ushort o, ushort n)
        {
            if (pin < 0) ((IWatchesOutputs)prim).OnOutputChanged(this, -pin, n);
            else prim.OnAnalog(this, pin, o, n);
        }
        public override void OnSerial(int pin, string o, string n)
        {
            if (pin < 0) ((IWatchesOutputs)prim).OnOutputChanged(this, -pin, n);
            else prim.OnSerial(this, pin, o, n);
        }

        BinSymbol IPrimitiveContext.Symbol => Symbol;
        public int InputCount => Symbol.Inputs.Length;
        public int OutputCount => Symbol.Outputs.Length;
        private int In(int pin) => pin >= 1 && pin <= Symbol.Inputs.Length ? Symbol.Inputs[pin - 1] : 0;
        public object Input(int pin) => Runtime.Value(In(pin));
        public bool Digital(int pin) => Runtime.DigitalAt(In(pin));
        public ushort Analog(int pin) => Runtime.AnalogAt(In(pin));
        public string Serial(int pin) => Runtime.SerialAt(In(pin));
        public bool IsConstant(int pin) => In(pin) <= 1;

        public void Set(int pin, object value)
        {
            if (pin < 1 || pin > Symbol.Outputs.Length) return;
            Runtime.Drive(Symbol.Outputs[pin - 1], value, Symbol.Path);
        }
        public void Set(int pin, bool value)
        {
            if (pin < 1 || pin > Symbol.Outputs.Length) return;
            Runtime.Drive(Symbol.Outputs[pin - 1], value, Symbol.Path);
        }
        public void Set(int pin, ushort value)
        {
            if (pin < 1 || pin > Symbol.Outputs.Length) return;
            Runtime.Drive(Symbol.Outputs[pin - 1], value, Symbol.Path);
        }
        public void Set(int pin, string? value)
        {
            if (pin < 1 || pin > Symbol.Outputs.Length) return;
            Runtime.Drive(Symbol.Outputs[pin - 1], value, Symbol.Path);
        }

        public void SetFromInput(int pin, int inputPin)
        {
            var sig = In(inputPin);
            switch (Runtime.KindOf(sig))
            {
                case SigKind.Digital: Set(pin, Runtime.DigitalAt(sig)); break;
                case SigKind.Analog: Set(pin, Runtime.AnalogAt(sig)); break;
                default: Set(pin, Runtime.SerialAt(sig)); break;
            }
        }

        public object Output(int pin) => pin >= 1 && pin <= Symbol.Outputs.Length ? Runtime.Value(Symbol.Outputs[pin - 1]) : false;
        public SigKind OutputSignalKind(int pin) => pin >= 1 && pin <= Symbol.Outputs.Length ? Runtime.KindOf(Symbol.Outputs[pin - 1]) : SigKind.Digital;

        public int ParamCount => Symbol.Params.Count;
        public long Number(int index, long fallback = 0) =>
            index >= 0 && index < Symbol.Params.Count && Symbol.Params[index].Number is int v ? v : fallback;
        public string? Text(int index) => index >= 0 && index < Symbol.Params.Count ? Symbol.Params[index].Text : null;

        public IDisposable AfterTicks(long ticks, Action action) => Runtime.After(ticks * 10, action);
        public void NextWave(Action action) => Runtime.Defer(action);
        public object? Recall(int pin) => Runtime._store.Get($"{Symbol.Path}#{pin}");
        public void Remember(int pin, object value) => Runtime._store.Set($"{Symbol.Path}#{pin}", value);
        public object? RecallKey(string key) => Runtime._store.Get(key);
        public void RememberKey(string key, object value) => Runtime._store.Set(key, value);
        public ProgramSlot Slot => Runtime._slot;
    }

    /// <summary>
    /// An IP device (touch panel, XPanel, EISC, DM/NVX endpoint). Inputs are feedback to the device and
    /// outputs come from it. Fields 10/12 split the inputs into digital, analog and serial joins; 11/13
    /// do the same for outputs. Within each group, position n is join n.
    /// </summary>
    private sealed class DeviceNode : Node
    {
        private readonly uint _ipId;
        private readonly int _inDigital, _inAnalogEnd, _outDigital, _outAnalogEnd;
        private JoinDevice? _device;
        private bool _eisc;
        private readonly string _source; // trace source for presses, built once instead of per event
        public JoinDevice? Device => _device;
        public Dictionary<uint, SmartObjectNode> SmartObjects { get; } = new();

        public DeviceNode(LpzRuntime rt, BinSymbol sym, uint ipId) : base(rt, sym)
        {
            _ipId = ipId;
            _source = $"IPID{ipId:X2}";
            _inDigital = Math.Min(sym.FieldInt(10) ?? sym.Inputs.Length, sym.Inputs.Length);
            _inAnalogEnd = Math.Clamp(sym.FieldInt(12) ?? _inDigital, _inDigital, sym.Inputs.Length);
            _outDigital = Math.Min(sym.FieldInt(11) ?? sym.Outputs.Length, sym.Outputs.Length);
            _outAnalogEnd = Math.Clamp(sym.FieldInt(13) ?? _outDigital, _outDigital, sym.Outputs.Length);
        }

        private JoinKey InJoin(int pin) =>
            pin <= _inDigital ? new(SigKind.Digital, (uint)pin)
            : pin <= _inAnalogEnd ? new(SigKind.Analog, (uint)(pin - _inDigital))
            : new(SigKind.Serial, (uint)(pin - _inAnalogEnd));

        private int OutPin(JoinKey k) => k.Kind switch
        {
            SigKind.Digital => k.Join <= _outDigital ? (int)k.Join : 0,
            SigKind.Analog => _outDigital + (int)k.Join <= _outAnalogEnd ? _outDigital + (int)k.Join : 0,
            _ => _outAnalogEnd + (int)k.Join <= Symbol.Outputs.Length ? _outAnalogEnd + (int)k.Join : 0
        };

        public override SigKind? InputKind(int pin) => InJoin(pin).Kind;
        public override SigKind? OutputKind(int pin) =>
            pin <= _outDigital ? SigKind.Digital : pin <= _outAnalogEnd ? SigKind.Analog : SigKind.Serial;

        public void Attach()
        {
            var model = Runtime._pkg.DeviceModels.GetValueOrDefault((int)_ipId) ?? "IP device";
            var address = Runtime._pkg.IpTable.FirstOrDefault(e => e.IpId == _ipId)?.Address ?? "";
            _eisc = model.Contains("Intersystem", StringComparison.OrdinalIgnoreCase) ||
                    model.Contains("EISC", StringComparison.OrdinalIgnoreCase) ||
                    model.Contains("InterProgram", StringComparison.OrdinalIgnoreCase) ||
                    address == "127.0.0.2";
            _device = Runtime._slot.RegisterDevice(_ipId, _eisc ? "EISC" : model, Symbol.Path);
            _device.InputReceived += OnDeviceInput;
            if (_eisc) Runtime._slot.Processor.Bus.Attach(_device);
            for (int pin = 1; pin <= Symbol.Inputs.Length; pin++)
                if (Symbol.Inputs[pin - 1] > 1) Feedback(pin, Runtime.Value(Symbol.Inputs[pin - 1]));
            foreach (var so in SmartObjects.Values) so.PushAll();
        }

        public void Detach()
        {
            if (_device == null) return;
            _device.InputReceived -= OnDeviceInput;
            if (_eisc) Runtime._slot.Processor.Bus.Detach(_device);
            _device = null;
        }

        public override void OnInput(int pin, object o, object n) => Feedback(pin, n);

        private void Feedback(int pin, object value) => _device?.SetFeedback(InJoin(pin), value);

        private void OnDeviceInput(JoinDevice dev, JoinKey key, object value)
        {
            if (key.SmartObject != 0)
            {
                if (SmartObjects.TryGetValue(key.SmartObject, out var so)) so.FromPanel(key, value);
                return;
            }
            var pin = OutPin(key);
            if (pin < 1) return;
            var sig = Symbol.Outputs[pin - 1];
            if (sig > 1) Runtime._slot.Dispatcher.Post(static (n, v, s) => { var d = (DeviceNode)n!; d.Runtime.Drive(s, v!, d._source); }, this, value, sig);
        }
    }
}
