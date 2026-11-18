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

    private readonly Dictionary<int, object> _values = new();
    private readonly Dictionary<int, SigKind> _kinds = new();
    private readonly Dictionary<int, List<(Node node, int pin)>> _fanout = new();
    private readonly List<Node> _nodes = new();
    private readonly List<DeviceNode> _devices = new();
    private readonly Queue<(int handle, object value, string source)> _pending = new();
    private readonly TimerSet _timers = new();
    private bool _solving;
    private bool _running;

    public SimplRuntime(ProgramSlot slot, SmwFile file, SymbolLibrary library, SimplOptions options)
    {
        _slot = slot;
        _file = file;
        _library = library;
        _options = options;
        Build();
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
            if (!_values.ContainsKey(sg.Handle)) SetKind(sg.Handle, SigKind.Digital);
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
            if (!_fanout.TryGetValue(h, out var list)) _fanout[h] = list = new();
            list.Add((node, pin));
        }
        for (int pin = 1; pin <= node.Symbol.Outputs.Count; pin++)
        {
            var h = node.Symbol.Outputs[pin - 1];
            if (h > 0) SetKind(h, node.OutputKind(pin));
        }
    }

    private void SetKind(int handle, SigKind kind)
    {
        if (_kinds.ContainsKey(handle)) return;
        _kinds[handle] = kind;
        _values[handle] = SigValue.Default(kind);
    }

    // ------------------------------------------------------------------ signals

    internal object Value(int handle) => handle > 0 && _values.TryGetValue(handle, out var v) ? v : false;

    internal SigKind KindOf(int handle) => _kinds.TryGetValue(handle, out var k) ? k : SigKind.Digital;

    /// <summary>Queue a signal change and run logic until everything settles. Must be on the slot thread.</summary>
    internal void Drive(int handle, object value, string source)
    {
        if (handle <= 0 || IsConstant(handle)) return;
        _pending.Enqueue((handle, SigValue.Coerce(KindOf(handle), value), source));
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
                var (h, v, src) = _pending.Dequeue();
                var old = Value(h);
                var kind = KindOf(h);
                if (SigValue.AreEqual(old, v) && kind != SigKind.Serial) continue;
                _values[h] = v;
                if (_slot.TraceWanted) _slot.Trace(_file.SignalName(h), kind, v, src);
                if (!_fanout.TryGetValue(h, out var listeners)) continue;
                foreach (var (node, pin) in listeners)
                {
                    try { node.OnInput(pin, old, v); }
                    catch (Exception e) { _slot.Log("Error", $"{node.Symbol}: {e.Message}"); }
                }
            }
        }
        finally { _solving = false; }
    }

    /// <summary>Signals named "0" and "1" are constants; "1" goes high at startup, which programs use as a start pulse.</summary>
    private bool IsConstant(int handle) => _file.Signals.TryGetValue(handle, out var s) && s.Name is "0" or "1";

    internal IDisposable After(long ms, Action action)
    {
        return _timers.After(ms, () => _slot.Dispatcher.Post(() => { if (_running) action(); }));
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
                _values[sg.Handle] = true;
                if (_slot.TraceWanted) _slot.Trace("1", SigKind.Digital, true, "start");
                if (_fanout.TryGetValue(sg.Handle, out var listeners))
                    foreach (var (node, pin) in listeners) node.OnInput(pin, false, true);
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
            .Where(s => _values.ContainsKey(s.Handle))
            .Select(s => new SignalInfo(_file.SignalName(s.Handle), KindOf(s.Handle), Value(s.Handle)))
            .ToList();

    public bool TrySetSignal(string name, object value)
    {
        var sg = _file.Signals.Values.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (sg == null && name.StartsWith('#') && int.TryParse(name[1..], out var h)) sg = _file.Signals.GetValueOrDefault(h);
        if (sg == null) return false;
        _slot.Dispatcher.Post(() => Drive(sg.Handle, value, "debugger"));
        return true;
    }

    public void Dispose() => Stop();

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

        SmwSymbol ISymbolContext.Symbol => Symbol;
        public int InputCount => Symbol.Inputs.Count;
        public int OutputCount => Symbol.Outputs.Count;
        private int In(int pin) => pin >= 1 && pin <= Symbol.Inputs.Count ? Symbol.Inputs[pin - 1] : 0;
        public bool IsConnected(int pin) => In(pin) > 0;
        public bool Digital(int pin) => Runtime.Value(In(pin)) is true;
        public ushort Analog(int pin) => Runtime.Value(In(pin)) is ushort u ? u : (ushort)0;
        public string Serial(int pin) => Runtime.Value(In(pin)) as string ?? string.Empty;

        public void SetOutput(int pin, object value)
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
            if (handle > 0) Runtime._slot.Dispatcher.Post(() => Runtime.Drive(handle, value, $"IPID{dev.IpId:X2}"));
        }
    }
}
