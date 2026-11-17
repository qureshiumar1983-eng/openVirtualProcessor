using OpenVP.Core;

namespace OpenVP.Lpz;

/// <summary>Loads compiled SIMPL Windows programs (.lpz).</summary>
public sealed class LpzLoader : IProgramLoader
{
    public bool CanLoad(string path) => LpzInspect.IsLpz(path);

    public IProgramRuntime Load(string path, ProgramSlot slot)
    {
        var pkg = LpzPackage.Open(path);
        if (pkg.Signals == null)
        {
            var sigPath = Path.ChangeExtension(path, ".sig");
            if (File.Exists(sigPath)) pkg.UseSignals(SigTable.Parse(File.ReadAllBytes(sigPath)));
        }
        return new LpzRuntime(slot, pkg);
    }
}

/// <summary>
/// Runs the compiled logic of a SIMPL Windows program. Each record becomes a node; when a signal
/// changes, every node reading it runs, and its outputs drive further signals until the logic settles.
/// Everything happens on the slot thread, one change at a time.
/// </summary>
public sealed class LpzRuntime : IProgramRuntime
{
    private const int MaxStepsPerSolve = 500_000;

    private readonly ProgramSlot _slot;
    private readonly LpzPackage _pkg;
    private readonly object[] _values;
    private readonly SigKind[] _kinds;
    private readonly bool[] _kindKnown;
    private readonly List<(Node node, int pin)>[] _fanout;
    private readonly bool[] _used;
    private readonly List<Node> _nodes = new();
    private readonly List<DeviceNode> _devices = new();
    private readonly Queue<(int sig, object value, string source)> _pending = new();
    private readonly Queue<Action> _nextWave = new();
    private readonly List<IDisposable> _timers = new();
    private readonly NvramStore _store;
    private bool _solving;
    private volatile bool _running;

    public LpzRuntime(ProgramSlot slot, LpzPackage pkg)
    {
        _slot = slot;
        _pkg = pkg;
        _store = new NvramStore(Path.Combine(slot.Directory, "nvram.json"));
        var count = Math.Max(2, pkg.Logic.MaxSignal + 1);
        _values = new object[count];
        _kinds = new SigKind[count];
        _kindKnown = new bool[count];
        _fanout = new List<(Node, int)>[count];
        _used = new bool[count];

        // Signal types: from the .sig when we have it, then from what the primitives imply.
        if (pkg.Signals != null)
            foreach (var e in pkg.Signals.Entries.Values)
                if (e.Index < count && e.Kind is SigFileKind.Digital or SigFileKind.Analog or SigFileKind.Serial)
                {
                    _kinds[e.Index] = (SigKind)(int)e.Kind;
                    _kindKnown[e.Index] = true;
                }

        Build();
        for (int i = 0; i < count; i++) _values[i] = SigValue.Default(_kinds[i]);
        _values[1] = true; // constant "1"
    }

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
                    Register(dev);
                }
                else if (sym.Type == -48)
                    Count(sym.Type); // SIMPL+ module: not yet
                continue;
            }
            if (sym.Type == 31) continue; // compile header
            var prim = PrimitiveLibrary.Create(sym.Type);
            if (prim == null) { Count(sym.Type); continue; }
            Register(new LogicNode(this, sym, prim));
        }

        void Count(int t) => Unsupported[t] = Unsupported.GetValueOrDefault(t) + 1;
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
            (_fanout[s] ??= new()).Add((node, pin));
        }
        for (int pin = 1; pin <= node.Symbol.Outputs.Length; pin++)
        {
            var s = node.Symbol.Outputs[pin - 1];
            if (s <= 1) continue;
            _used[s] = true;
            Infer(s, node.OutputKind(pin));
            if (node.WatchesOutputs) (_fanout[s] ??= new()).Add((node, -pin)); // negative pin = own output
        }
    }

    private void Infer(int sig, SigKind? kind)
    {
        if (kind is not SigKind k || _kindKnown[sig]) return;
        _kinds[sig] = k;
        _kindKnown[sig] = true;
    }

    // ------------------------------------------------------------------ signals

    internal object Value(int sig) => sig >= 0 && sig < _values.Length ? _values[sig] : false;
    internal SigKind KindOf(int sig) => sig >= 0 && sig < _kinds.Length ? _kinds[sig] : SigKind.Digital;

    internal string NameOf(int sig) =>
        _pkg.Signals?.NameOf(sig) is string n ? (n.StartsWith("::") ? n[2..] : n) : $"#{sig}";

    /// <summary>Queue a change and solve until settled. Slot thread only.</summary>
    internal void Drive(int sig, object value, string source)
    {
        if (sig <= 1 || sig >= _values.Length) return; // constants and unconnected
        // An unconnected input reads the constant signal 0/1; into a serial signal that means "empty".
        if (value is bool && _kinds[sig] == SigKind.Serial) value = string.Empty;
        _pending.Enqueue((sig, SigValue.Coerce(_kinds[sig], value), source));
        Solve();
    }

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
                var (s, v, src) = _pending.Dequeue();
                var old = _values[s];
                if (SigValue.AreEqual(old, v) && _kinds[s] != SigKind.Serial) continue;
                _values[s] = v;
                _slot.Trace(NameOf(s), _kinds[s], v, src);
                var listeners = _fanout[s];
                if (listeners == null) continue;
                foreach (var (node, pin) in listeners)
                {
                    try { node.OnInput(pin, old, v); }
                    catch (Exception e) { _slot.Log("Error", $"{node.Symbol}: {e.Message}"); }
                }
            }
        }
        finally { _solving = false; }
    }

    internal void Defer(Action a)
    {
        _nextWave.Enqueue(a);
        if (!_solving) _slot.Dispatcher.Post(Solve);
    }

    internal IDisposable After(long ms, Action action)
    {
        var t = new Timer(_ => _slot.Dispatcher.Post(() => { if (_running) action(); }), null, Math.Max(0, ms), Timeout.Infinite);
        lock (_timers) _timers.Add(t);
        return t;
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
                if (_fanout[1] is { } ones)
                    foreach (var (node, pin) in ones) node.OnInput(pin, false, true);
            }
            finally { _solving = false; }
            Solve();
        });

        var total = _pkg.Logic.Symbols.Count;
        _slot.Log("Notice", $"Compiled program: {_nodes.Count - _devices.Count} logic symbols and {_devices.Count} devices running" +
                             (_pkg.Signals == null ? " (no .sig: signals shown by number)" : ""));
        if (Unsupported.Count > 0)
            _slot.Log("Warning", $"Not supported yet: " + string.Join(", ",
                Unsupported.OrderByDescending(p => p.Value).Select(p => $"type {p.Key}{(p.Key == -48 ? " (SIMPL+)" : "")} ×{p.Value}")));
    }

    public void Stop()
    {
        _running = false;
        _store.Flush();
        lock (_timers)
        {
            foreach (var t in _timers) t.Dispose();
            _timers.Clear();
        }
        foreach (var d in _devices) d.Detach();
    }

    public IReadOnlyList<SignalInfo> GetSignals()
    {
        var list = new List<SignalInfo>();
        if (_pkg.Signals != null)
        {
            foreach (var e in _pkg.Signals.Entries.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
                if (e.Index > 1 && e.Index < _values.Length && !e.Name.StartsWith("::"))
                    list.Add(new SignalInfo(e.Name, _kinds[e.Index], _values[e.Index]));
        }
        else
        {
            for (int i = 2; i < _values.Length; i++)
                if (_used[i]) list.Add(new SignalInfo($"#{i}", _kinds[i], _values[i]));
        }
        return list;
    }

    public bool TrySetSignal(string name, object value)
    {
        int sig = -1;
        if (name.StartsWith('#') && int.TryParse(name[1..], out var n)) sig = n;
        else if (_pkg.Signals != null)
            sig = _pkg.Signals.Entries.Values.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Index ?? -1;
        if (sig <= 1 || sig >= _values.Length) return false;
        _slot.Dispatcher.Post(() => Drive(sig, value, "debugger"));
        return true;
    }

    public void Dispose() => Stop();

    // ------------------------------------------------------------------ nodes

    internal abstract class Node(LpzRuntime rt, BinSymbol sym)
    {
        public LpzRuntime Runtime { get; } = rt;
        public BinSymbol Symbol { get; } = sym;
        public abstract SigKind? InputKind(int pin);
        public abstract SigKind? OutputKind(int pin);
        public virtual bool WatchesOutputs => false;
        public virtual void Start() { }
        public abstract void OnInput(int pin, object oldValue, object newValue);
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

        BinSymbol IPrimitiveContext.Symbol => Symbol;
        public int InputCount => Symbol.Inputs.Length;
        public int OutputCount => Symbol.Outputs.Length;
        private int In(int pin) => pin >= 1 && pin <= Symbol.Inputs.Length ? Symbol.Inputs[pin - 1] : 0;
        public object Input(int pin) => Runtime.Value(In(pin));
        public bool Digital(int pin) => Input(pin) is true;
        public ushort Analog(int pin) => Input(pin) is ushort u ? u : (ushort)0;
        public string Serial(int pin) => Input(pin) as string ?? string.Empty;
        public bool IsConstant(int pin) => In(pin) <= 1;

        public void Set(int pin, object value)
        {
            if (pin < 1 || pin > Symbol.Outputs.Length) return;
            Runtime.Drive(Symbol.Outputs[pin - 1], value, Symbol.Path);
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

        public DeviceNode(LpzRuntime rt, BinSymbol sym, uint ipId) : base(rt, sym)
        {
            _ipId = ipId;
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
            var pin = OutPin(key);
            if (pin < 1) return;
            var sig = Symbol.Outputs[pin - 1];
            if (sig > 1) Runtime._slot.Dispatcher.Post(() => Runtime.Drive(sig, value, $"IPID{dev.IpId:X2}"));
        }
    }
}
