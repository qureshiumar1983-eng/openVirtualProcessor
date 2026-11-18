using System.Text.Json;
using OpenVP.Core;

namespace OpenVP.Simpl;

/// <summary>Loads SIMPL Windows programs from their .smw source.</summary>
public sealed class SimplLoader : IProgramLoader
{
    private readonly string? _symbolMapPath;

    /// <param name="symbolMapPath">Optional symbols.json mapping SmC codes to behavior names.</param>
    public SimplLoader(string? symbolMapPath = null) { _symbolMapPath = symbolMapPath; }

    public bool CanLoad(string path) => Path.GetExtension(path).Equals(".smw", StringComparison.OrdinalIgnoreCase);

    public IProgramRuntime Load(string path, ProgramSlot slot)
    {
        var lib = new SymbolLibrary();
        if (_symbolMapPath != null) lib.LoadMap(_symbolMapPath);
        lib.LoadMap(Path.ChangeExtension(path, ".symbols.json"));
        var options = SimplOptions.Load(Path.ChangeExtension(path, ".openvp.json"));
        return new SimplRuntime(slot, SmwFile.Load(path), lib, options);
    }
}

/// <summary>
/// Per-program settings that the .smw doesn't make clear, in a sidecar file next to it
/// (MyProgram.openvp.json). Today: IP IDs for device symbols, keyed by symbol handle.
/// { "ipids": { "21": "03" } }
/// </summary>
public sealed class SimplOptions
{
    public Dictionary<string, string> Ipids { get; set; } = new();

    public static SimplOptions Load(string path)
    {
        if (!File.Exists(path)) return new SimplOptions();
        return JsonSerializer.Deserialize<SimplOptions>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new SimplOptions();
    }
}

/// <summary>
/// Runs a SIMPL Windows program by interpreting its symbols. Signals carry values; when a signal changes,
/// every symbol with that signal on an input is told, and their outputs drive further signals. All of it
/// happens on the slot thread, one change at a time, in the order changes were made.
/// </summary>
public sealed class SimplRuntime : IProgramRuntime
{
    private const int MaxStepsPerSolve = 200_000;

    private readonly ProgramSlot _slot;
    private readonly SmwFile _file;
    private readonly SymbolLibrary _library;
    private readonly SimplOptions _options;

    // Signal handles are small integers, so everything is indexed by handle in arrays sized to the largest
    // one. Values live in one typed array per kind; _kinds[h] says which is in use (the other two stay at
    // their defaults for that handle). Nothing on the hot path boxes.
    private readonly bool[] _dig;
    private readonly ushort[] _ana;
    private readonly string[] _ser;
    private readonly SigKind[] _kinds;
    private readonly bool[] _kindKnown;  // the handle has a signal (a pin or an Sg block gave it a kind)
    private readonly bool[] _constant;   // signals named "0" and "1"
    // Listeners per handle, in registration order. Built as lists while the program loads, then flattened:
    // handle h's listeners are _edges[_edgeStart[h] .. _edgeStart[h + 1]).
    private List<(Node node, int pin)>[]? _fanout;
    private Edge[] _edges = Array.Empty<Edge>();
    private int[] _edgeStart = Array.Empty<int>();
    private readonly List<Node> _nodes = new();
    private readonly List<DeviceNode> _devices = new();
    private readonly Queue<Pending> _pending = new();
    private readonly TimerSet _timers;
    private bool _solving;
    private volatile bool _running;

    public SimplRuntime(ProgramSlot slot, SmwFile file, SymbolLibrary library, SimplOptions options)
    {
        _slot = slot;
        _file = file;
        _library = library;
        _options = options;
        // One OS timer for the whole slot; due timers reach the slot thread in one post (see TimerSet).
        _timers = new TimerSet(() => slot.Dispatcher, () => _running, e => slot.Log("Error", $"Unhandled exception in program: {e}"));

        var max = 1;
        foreach (var h in file.Signals.Keys) max = Math.Max(max, h);
        foreach (var sym in file.Symbols.Values)
        {
            foreach (var h in sym.Inputs) max = Math.Max(max, h);
            foreach (var h in sym.Outputs) max = Math.Max(max, h);
        }
        if (max > MaxHandle) throw new InvalidDataException($"Signal handle {max} is larger than the {MaxHandle:N0} OpenVP supports.");
        var count = max + 1;
        _dig = new bool[count];
        _ana = new ushort[count];
        _ser = new string[count];
        _kinds = new SigKind[count];
        _kindKnown = new bool[count];
        _constant = new bool[count];
        _fanout = new List<(Node, int)>[count];
        foreach (var sg in file.Signals.Values)
            if (sg.Handle > 0 && sg.Name is "0" or "1") _constant[sg.Handle] = true;

        Build();
        Array.Fill(_ser, string.Empty);
        Flatten();
    }

    // Arrays cost about 20 bytes per handle; real programs use thousands. This only stops a corrupt file from asking for gigabytes.
    private const int MaxHandle = 1 << 24;

    /// <summary>One listener of a signal: the node and which of its pins reads it.</summary>
    private readonly record struct Edge(Node Node, int Pin);

    /// <summary>A queued change. Digital and analog keep their value in Num, serial in Str.</summary>
    private readonly record struct Pending(int Handle, uint Num, string? Str, string Source);

    private void Flatten()
    {
        var lists = _fanout!;
        _edgeStart = new int[lists.Length + 1];
        var total = 0;
        for (int h = 0; h < lists.Length; h++)
        {
            _edgeStart[h] = total;
            total += lists[h]?.Count ?? 0;
        }
        _edgeStart[lists.Length] = total;
        _edges = new Edge[total];
        for (int h = 0; h < lists.Length; h++)
        {
            if (lists[h] is not { } list) continue;
            for (int i = 0; i < list.Count; i++) _edges[_edgeStart[h] + i] = new Edge(list[i].node, list[i].pin);
        }
        _fanout = null; // only needed while building
    }

    public string Kind => "SIMPL Windows";
    public string ProgramName => _file.ProgramName;

    /// <summary>Symbols the interpreter has no behavior for (shown as warnings at load).</summary>
    public List<SmwSymbol> Unsupported { get; } = new();

    // ------------------------------------------------------------------ build

    private void Build()
    {
        foreach (var sym in _file.Symbols.Values.OrderBy(s => s.Handle))
        {
            if (IsDevice(sym))
            {
                var dev = new DeviceNode(this, sym);
                _devices.Add(dev);
                Register(dev);
                continue;
            }

            var behavior = _library.Create(sym);
            if (behavior == null)
            {
                // Structural symbols (processor, slots, folders) have no pins wired; only warn about the rest.
                if (sym.Inputs.Any(h => h > 0) || sym.Outputs.Any(h => h > 0))
                {
                    Unsupported.Add(sym);
                    _slot.Log("Warning", $"No behavior for symbol {sym} yet; its outputs stay at their defaults.");
                }
                continue;
            }
            Register(new LogicNode(this, sym, behavior));
        }

        foreach (var sg in _file.Signals.Values)
            if (sg.Handle > 0) SetKind(sg.Handle, SigKind.Digital);
    }

    private static bool IsDevice(SmwSymbol s) =>
        s.HasGroups && s.DeviceHandle > 0 && (s.InputGroups.Sum() > 0 || s.OutputGroups.Sum() > 0);

    private void Register(Node node)
    {
        _nodes.Add(node);
        for (int pin = 1; pin <= node.Symbol.Inputs.Count; pin++)
        {
            var h = node.Symbol.Inputs[pin - 1];
            if (h <= 0) continue;
            SetKind(h, node.InputKind(pin));
            (_fanout![h] ??= new()).Add((node, pin));
        }
        for (int pin = 1; pin <= node.Symbol.Outputs.Count; pin++)
        {
            var h = node.Symbol.Outputs[pin - 1];
            if (h > 0) SetKind(h, node.OutputKind(pin));
        }
    }

    private void SetKind(int handle, SigKind kind)
    {
        if (_kindKnown[handle]) return; // the first pin to claim a signal decides its kind
        _kinds[handle] = kind;
        _kindKnown[handle] = true;
    }

    // ------------------------------------------------------------------ signals

    /// <summary>The value as an object, for devices and the debugger. Digital values use the shared boxes.</summary>
    internal object Value(int handle)
    {
        if ((uint)handle >= (uint)_kinds.Length || handle == 0) return SigValue.False;
        return _kinds[handle] switch
        {
            SigKind.Digital => SigValue.Box(_dig[handle]),
            SigKind.Analog => _ana[handle],
            _ => _ser[handle]
        };
    }

    // A handle only ever writes the array of its own kind, so reading another kind's array gives that
    // kind's default (false, 0, ""): the same answers as the old "is true / is ushort / as string" checks.
    internal bool DigitalAt(int handle) => (uint)handle < (uint)_dig.Length && _dig[handle];
    internal ushort AnalogAt(int handle) => (uint)handle < (uint)_ana.Length ? _ana[handle] : (ushort)0;
    internal string SerialAt(int handle) => (uint)handle < (uint)_ser.Length ? _ser[handle] : string.Empty;

    internal SigKind KindOf(int handle) => (uint)handle < (uint)_kinds.Length ? _kinds[handle] : SigKind.Digital;

    // The Drive overloads queue a change and run logic until everything settles (slot thread only). Each
    // converts the value to the signal's kind exactly as SigValue.Coerce does, without boxing.

    internal void Drive(int handle, bool value, string source)
    {
        if (handle <= 0 || handle >= _kinds.Length || _constant[handle]) return;
        switch (_kinds[handle])
        {
            case SigKind.Digital: Enqueue(handle, value ? 1u : 0u, null, source); break;
            case SigKind.Analog: Enqueue(handle, value ? 1u : 0u, null, source); break;
            default: Enqueue(handle, 0, value ? "True" : "False", source); break;
        }
        Solve();
    }

    internal void Drive(int handle, ushort value, string source)
    {
        if (handle <= 0 || handle >= _kinds.Length || _constant[handle]) return;
        switch (_kinds[handle])
        {
            case SigKind.Digital: Enqueue(handle, value != 0 ? 1u : 0u, null, source); break;
            case SigKind.Analog: Enqueue(handle, value, null, source); break;
            default: Enqueue(handle, 0, value.ToString(), source); break;
        }
        Solve();
    }

    internal void Drive(int handle, string? value, string source)
    {
        if (handle <= 0 || handle >= _kinds.Length || _constant[handle]) return;
        switch (_kinds[handle])
        {
            case SigKind.Digital:
                Enqueue(handle, value != null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase)) ? 1u : 0u, null, source);
                break;
            case SigKind.Analog: Enqueue(handle, ushort.TryParse(value, out var u) ? u : (ushort)0, null, source); break;
            default: Enqueue(handle, 0, value ?? string.Empty, source); break;
        }
        Solve();
    }

    /// <summary>For values that arrive as objects (panels, the debugger): the three known types take the typed path.</summary>
    internal void Drive(int handle, object? value, string source)
    {
        switch (value)
        {
            case bool b: Drive(handle, b, source); return;
            case ushort u: Drive(handle, u, source); return;
            case string s: Drive(handle, s, source); return;
        }
        if (handle <= 0 || handle >= _kinds.Length || _constant[handle]) return;
        var kind = _kinds[handle];
        var v = SigValue.Coerce(kind, value); // other numeric types, null
        switch (kind)
        {
            case SigKind.Digital: Enqueue(handle, (bool)v ? 1u : 0u, null, source); break;
            case SigKind.Analog: Enqueue(handle, (ushort)v, null, source); break;
            default: Enqueue(handle, 0, (string)v, source); break;
        }
        Solve();
    }

    private void Enqueue(int handle, uint num, string? str, string source) => _pending.Enqueue(new Pending(handle, num, str, source));

    private void Solve()
    {
        if (_solving) return;
        _solving = true;
        try
        {
            var steps = 0;
            while (_pending.Count > 0)
            {
                if (++steps > MaxStepsPerSolve)
                {
                    _pending.Clear();
                    _slot.Log("Error", "Logic did not settle (feedback loop?). Stopped after 200,000 steps.");
                    break;
                }
                var change = _pending.Dequeue();
                var h = change.Handle;
                int first = _edgeStart[h], end = _edgeStart[h + 1];
                switch (_kinds[h])
                {
                    case SigKind.Digital:
                    {
                        var v = change.Num != 0;
                        var old = _dig[h];
                        if (old == v) continue;
                        _dig[h] = v;
                        if (_slot.TraceWanted) _slot.Trace(_file.SignalName(h), SigKind.Digital, SigValue.Box(v), change.Source);
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
                        var old = _ana[h];
                        if (old == v) continue;
                        _ana[h] = v;
                        if (_slot.TraceWanted) _slot.Trace(_file.SignalName(h), SigKind.Analog, v, change.Source);
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
                        var old = _ser[h];
                        _ser[h] = v;
                        if (_slot.TraceWanted) _slot.Trace(_file.SignalName(h), SigKind.Serial, v, change.Source);
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

    internal IDisposable After(long ms, Action action)
    {
        return _timers.After(ms, action);
    }

    // ------------------------------------------------------------------ lifecycle

    public void Start()
    {
        _slot.Dispatcher.Invoke(() =>
        {
            _running = true;
            foreach (var d in _devices) d.Attach();
            foreach (var n in _nodes) n.Start();

            // Raise the "1" constant: every symbol wired to it sees a rising edge, like a real program start.
            foreach (var sg in _file.Signals.Values.Where(s => s.Name == "1"))
            {
                _dig[sg.Handle] = true;
                if (_slot.TraceWanted) _slot.Trace("1", SigKind.Digital, true, "start");
                for (int e = _edgeStart[sg.Handle]; e < _edgeStart[sg.Handle + 1]; e++)
                    _edges[e].Node.OnDigital(_edges[e].Pin, false, true);
            }
        });
        if (Unsupported.Count > 0)
            _slot.Log("Warning", $"{Unsupported.Count} symbol(s) have no behavior yet: " +
                string.Join(", ", Unsupported.Select(s => $"{s.Name} (SmC {s.Code})").Distinct().Take(20)));
    }

    public void Stop()
    {
        _running = false;
        _timers.CancelAll();
        foreach (var d in _devices) d.Detach();
    }

    public IReadOnlyList<SignalInfo> GetSignals() =>
        _file.Signals.Values.OrderBy(s => s.Handle)
            .Where(s => s.Handle > 0 && s.Handle < _kindKnown.Length && _kindKnown[s.Handle])
            .Select(s => new SignalInfo(_file.SignalName(s.Handle), KindOf(s.Handle), Value(s.Handle)))
            .ToList();

    public bool TrySetSignal(string name, object value)
    {
        var sg = SignalNamed(name);
        if (sg == null) return false;
        _slot.Dispatcher.Post(() => Drive(sg.Handle, value, "debugger"));
        return true;
    }

    public bool TryPulseSignal(string name)
    {
        var sg = SignalNamed(name);
        if (sg == null || KindOf(sg.Handle) != SigKind.Digital) return false;
        // One post, so nothing (a timer, a panel press) can run between the two edges.
        _slot.Dispatcher.Post(() =>
        {
            Drive(sg.Handle, true, "debugger");
            Drive(sg.Handle, false, "debugger");
        });
        return true;
    }

    private SmwSignal? SignalNamed(string name)
    {
        var sg = _file.Signals.Values.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (sg == null && name.StartsWith('#') && int.TryParse(name[1..], out var h)) sg = _file.Signals.GetValueOrDefault(h);
        return sg;
    }

    public void Dispose()
    {
        Stop();
        _timers.Dispose();
    }

    // ------------------------------------------------------------------ nodes

    internal abstract class Node
    {
        protected Node(SimplRuntime rt, SmwSymbol sym) { Runtime = rt; Symbol = sym; }
        public SimplRuntime Runtime { get; }
        public SmwSymbol Symbol { get; }
        public abstract SigKind InputKind(int pin);
        public abstract SigKind OutputKind(int pin);
        public virtual void Start() { }
        public abstract void OnInput(int pin, object oldValue, object newValue);
        // Typed entry points the engine calls. The defaults box (digital uses the shared boxes) and call OnInput.
        public virtual void OnDigital(int pin, bool oldValue, bool newValue) => OnInput(pin, SigValue.Box(oldValue), SigValue.Box(newValue));
        public virtual void OnAnalog(int pin, ushort oldValue, ushort newValue) => OnInput(pin, oldValue, newValue);
        public virtual void OnSerial(int pin, string oldValue, string newValue) => OnInput(pin, oldValue, newValue);

        protected static SigKind FromGroups(int[] groups, int pin)
        {
            if (pin <= groups[0]) return SigKind.Digital;
            if (pin <= groups[0] + groups[1]) return SigKind.Analog;
            return SigKind.Serial;
        }
    }

    private sealed class LogicNode : Node, ISymbolContext
    {
        private readonly SymbolBehavior _behavior;

        public LogicNode(SimplRuntime rt, SmwSymbol sym, SymbolBehavior behavior) : base(rt, sym) { _behavior = behavior; }

        public override SigKind InputKind(int pin) => Symbol.HasGroups ? FromGroups(Symbol.InputGroups, pin) : _behavior.InputKind(pin);
        public override SigKind OutputKind(int pin) => Symbol.HasGroups ? FromGroups(Symbol.OutputGroups, pin) : _behavior.OutputKind(pin);
        public override void Start() => _behavior.Start(this);
        public override void OnInput(int pin, object o, object n) => _behavior.OnInput(this, pin, o, n);
        public override void OnDigital(int pin, bool o, bool n) => _behavior.OnDigital(this, pin, o, n);
        public override void OnAnalog(int pin, ushort o, ushort n) => _behavior.OnAnalog(this, pin, o, n);
        public override void OnSerial(int pin, string o, string n) => _behavior.OnSerial(this, pin, o, n);

        SmwSymbol ISymbolContext.Symbol => Symbol;
        public int InputCount => Symbol.Inputs.Count;
        public int OutputCount => Symbol.Outputs.Count;
        private int In(int pin) => pin >= 1 && pin <= Symbol.Inputs.Count ? Symbol.Inputs[pin - 1] : 0;
        public bool IsConnected(int pin) => In(pin) > 0;
        public bool Digital(int pin) => Runtime.DigitalAt(In(pin));
        public ushort Analog(int pin) => Runtime.AnalogAt(In(pin));
        public string Serial(int pin) => Runtime.SerialAt(In(pin));

        public void SetOutput(int pin, object value)
        {
            if (pin < 1 || pin > Symbol.Outputs.Count) return;
            Runtime.Drive(Symbol.Outputs[pin - 1], value, Symbol.Name);
        }
        public void SetOutput(int pin, bool value)
        {
            if (pin < 1 || pin > Symbol.Outputs.Count) return;
            Runtime.Drive(Symbol.Outputs[pin - 1], value, Symbol.Name);
        }
        public void SetOutput(int pin, ushort value)
        {
            if (pin < 1 || pin > Symbol.Outputs.Count) return;
            Runtime.Drive(Symbol.Outputs[pin - 1], value, Symbol.Name);
        }
        public void SetOutput(int pin, string value)
        {
            if (pin < 1 || pin > Symbol.Outputs.Count) return;
            Runtime.Drive(Symbol.Outputs[pin - 1], value, Symbol.Name);
        }

        public object GetOutput(int pin) =>
            pin >= 1 && pin <= Symbol.Outputs.Count ? Runtime.Value(Symbol.Outputs[pin - 1]) : false;

        public string Param(int index) => index >= 1 && index <= Symbol.Parameters.Count ? Symbol.Parameters[index - 1] : string.Empty;

        public long ParamNumber(int index, long fallback) => SmwFile.TryParseNumber(Param(index), out var v) ? v : fallback;

        public IDisposable After(long ms, Action action) => Runtime.After(ms, action);

        public void Warn(string message) => Runtime._slot.Log("Warning", $"{Symbol}: {message}");
    }

    /// <summary>
    /// A device symbol (touch panel, XPanel, EISC). Input pins are feedback joins to the device; output
    /// pins are joins coming from it. Within each group, pin n is join n.
    /// </summary>
    private sealed class DeviceNode : Node
    {
        private JoinDevice? _device;

        private string _source = "IPID"; // trace source for presses, built once at Attach instead of per event

        public DeviceNode(SimplRuntime rt, SmwSymbol sym) : base(rt, sym) { }

        public override SigKind InputKind(int pin) => FromGroups(Symbol.InputGroups, pin);
        public override SigKind OutputKind(int pin) => FromGroups(Symbol.OutputGroups, pin);

        private bool IsEisc =>
            Symbol.Name.Contains("Intersystem", StringComparison.OrdinalIgnoreCase) ||
            Symbol.Name.Contains("EISC", StringComparison.OrdinalIgnoreCase);

        public uint IpId
        {
            get
            {
                if (Runtime._options.Ipids.TryGetValue(Symbol.Handle.ToString(), out var s) &&
                    uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var fromFile))
                    return fromFile;
                // Real files: the symbol points at a Dv block (DvH) whose Ad= is the IP ID in hex.
                if (Runtime._file.Devices.TryGetValue(Symbol.DeviceHandle, out var dv) &&
                    uint.TryParse(dv.Get("Ad"), System.Globalization.NumberStyles.HexNumber, null, out var ad) && ad is > 0 and < 0xFF)
                    return ad;
                // Look for an explicit IPID parameter on the symbol or its parent slot.
                foreach (var sym in new[] { Symbol, Runtime._file.Symbols.GetValueOrDefault(Symbol.Parent) })
                {
                    var p = sym?.Raw.Get("IPID") ?? sym?.Raw.Get("IpId");
                    if (p != null && SmwFile.TryParseNumber(p.EndsWith('h') ? p : p + "h", out var v) && v is > 0 and < 0xFF)
                        return (uint)v;
                }
                // Otherwise give each device a stable ID derived from its handle, starting at 03.
                return (uint)(0x03 + Runtime._devices.IndexOf(this));
            }
        }

        public void Attach()
        {
            var slot = Runtime._slot;
            _device = slot.RegisterDevice(IpId, IsEisc ? "EISC" : Symbol.Name, $"SIMPL symbol #{Symbol.Handle}");
            _source = $"IPID{_device.IpId:X2}";
            _device.InputReceived += OnDeviceInput;
            if (IsEisc) slot.Processor.Bus.Attach(_device);
            // Push current feedback values so a panel that connects later sees them.
            for (int pin = 1; pin <= Symbol.Inputs.Count; pin++)
                if (Symbol.Inputs[pin - 1] > 0) Feedback(pin, Runtime.Value(Symbol.Inputs[pin - 1]));
        }

        public void Detach()
        {
            if (_device == null) return;
            _device.InputReceived -= OnDeviceInput;
            if (IsEisc) Runtime._slot.Processor.Bus.Detach(_device);
            _device = null;
        }

        public override void OnInput(int pin, object o, object n) => Feedback(pin, n);
        public override void OnDigital(int pin, bool o, bool n) => Feedback(pin, SigValue.Box(n));
        public override void OnAnalog(int pin, ushort o, ushort n) => Feedback(pin, n);
        public override void OnSerial(int pin, string o, string n) => Feedback(pin, n);

        private void Feedback(int pin, object value)
        {
            var g = Symbol.InputGroups;
            var kind = FromGroups(g, pin);
            var join = kind switch
            {
                SigKind.Digital => pin,
                SigKind.Analog => pin - g[0],
                _ => pin - g[0] - g[1]
            };
            _device?.SetFeedback(new JoinKey(kind, (uint)join), value);
        }

        private void OnDeviceInput(JoinDevice dev, JoinKey key, object value)
        {
            var g = Symbol.OutputGroups;
            var offset = key.Kind switch { SigKind.Digital => 0, SigKind.Analog => g[0], _ => g[0] + g[1] };
            var size = g[(int)key.Kind];
            if (key.Join < 1 || key.Join > size) return; // join not shown on the symbol
            var pin = offset + (int)key.Join;
            var handle = Symbol.Outputs.Count >= pin ? Symbol.Outputs[pin - 1] : 0;
            if (handle > 0) Runtime._slot.Dispatcher.Post(static (n, v, h) => { var d = (DeviceNode)n!; d.Runtime.Drive(h, v!, d._source); }, this, value, handle);
        }
    }
}
