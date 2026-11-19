using OpenVP.Core;

namespace OpenVP.Lpz;

/// <summary>Loads compiled SIMPL Windows programs (.lpz).</summary>
public sealed class LpzLoader : IProgramLoader
{
    private readonly string? _primitiveMapPath;

    /// <param name="primitiveMapPath">Optional primitives.json naming the symbol behind compiled primitive types
    /// the catalog doesn't know yet: <c>{ "301": "NAND" }</c>. A MyProgram.primitives.json next to the program
    /// is read too and wins.</param>
    public LpzLoader(string? primitiveMapPath = null) { _primitiveMapPath = primitiveMapPath; }

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
        else if (pkg.Signals == null)
            slot.Log("Warning", $"No signal names: the debugger will show signal numbers (#123). Signal names are in {Path.GetFileName(sigPath)}, which the compiler writes next to the .smw. Load it together with the .lpz.");
        var problems = new List<string>();
        var overrides = _primitiveMapPath != null ? LogicSymbolCatalog.LoadMap(_primitiveMapPath, problems) : new();
        foreach (var (k, v) in LogicSymbolCatalog.LoadMap(Path.ChangeExtension(path, ".primitives.json"), problems)) overrides[k] = v;
        foreach (var p in problems) slot.Log("Warning", p);
        return new LpzRuntime(slot, pkg, overrides);
    }
}

/// <summary>
/// Runs the compiled logic of a SIMPL Windows program. Each record becomes a node; when a signal
/// changes, every node reading it runs, and its outputs drive further signals until the logic settles.
/// Everything happens on the slot thread, one change at a time.
/// </summary>
public sealed partial class LpzRuntime : IProgramRuntime, IProgramGraphSource
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
    private readonly TimerSet _timers;
    private readonly NvramStore _store;
    private bool _solving;
    private volatile bool _running;
    // Causality (docs/debugger-paths.md section 2). Only touched while a debugger is listening (_slot.TraceWanted).
    // While Solve hands signal s to an edge's node, _dispOn is set and _disp* say who is acting and why, so an
    // Enqueue made by that node is stamped with the node's symbol id, s, depth + 1 and the chain.
    private bool _dispOn;
    private string? _dispSym;
    private int _dispCause;
    private int _dispDepth;
    private long _dispChain;
    private long _chainSeq;
    private readonly Dictionary<BinSymbol, string> _symIds = new();

    private readonly IReadOnlyDictionary<int, LogicSymbol>? _primitiveOverrides;

    public LpzRuntime(ProgramSlot slot, LpzPackage pkg, IReadOnlyDictionary<int, LogicSymbol>? primitiveOverrides = null)
    {
        _primitiveOverrides = primitiveOverrides;
        _slot = slot;
        _pkg = pkg;
        _store = new NvramStore(Path.Combine(slot.Directory, "nvram.json"));
        // One OS timer for the whole slot; due timers reach the slot thread in one post (see TimerSet).
        _timers = new TimerSet(() => slot.Dispatcher, () => _running, e => slot.Log("Error", $"Unhandled exception in program: {e}"));
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

        AssignSymbolIds();
        Build();
        Array.Fill(_ser, string.Empty);
        _dig[1] = true; // constant "1"
        Flatten();
    }

    /// <summary>One listener of a signal: the node and which of its pins reads it (negative = its own output).</summary>
    private readonly record struct Edge(Node Node, int Pin);

    /// <summary>A queued change. Digital and analog keep their value in Num, serial in Str.</summary>
    /// <remarks>Digital and analog values fit in 16 bits, so while tracing the high 16 bits of Num hold 1 + an index
    /// into _meta (causality); 0 means none. That keeps this struct at 24 bytes, which matters on the hot path.</remarks>
    private readonly record struct Pending(int Sig, uint Num, string? Str, string Source)
    {
        public uint Value => Num & 0xFFFF;
        public int Meta => (int)(Num >> 16) - 1;
    }

    private readonly record struct CauseInfo(string? Sym, int CauseSig, int Depth, long Chain);
    private CauseInfo[] _meta = new CauseInfo[16];
    private int _metaCount;
    // The change being processed (set by ReadMeta while tracing).
    private string? _chSym;
    private int _chCause, _chDepth;
    private long _chChain;

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
            var prim = PrimitiveLibrary.Create(sym.Type, _primitiveOverrides);
            if (prim == null) { Count(sym.Type); continue; }
            Register(LogicNode.Create(this, sym, prim));
        }

        void Count(int t) => Unsupported[t] = Unsupported.GetValueOrDefault(t) + 1;
    }

    private readonly HashSet<string> _moduleErrors = new();
    private SlotDispatcher? _callbacks;

    /// <summary>Load and instantiate the SIMPL+ module a −48 record points at.</summary>
    private SplusNode? CreateModule(BinSymbol sym)
    {
        var dll = sym.Params.Count > 1 ? sym.Params[1].Text : null;
        if (string.IsNullOrEmpty(dll))
        {
            _slot.ReportUnsupported("SIMPL+ module", "(record names no compiled .dll)", "type -48 record without a module file name", ProgramName);
            return null;
        }
        object? instance = null;
        try
        {
            if (_alc == null)
            {
                var dir = Path.Combine(_slot.Directory, "lpz");
                _pkg.ExtractAssemblies(dir);
                _alc = new LpzLoadContext(_slot.Number, dir, AppContext.BaseDirectory);
                // Point the SIMPL# runtime used by modules and their libraries at this slot.
                var shim = _alc.LoadFromAssemblyName(new System.Reflection.AssemblyName("OpenVP.SimplSharp"));
                var bridge = shim.GetType("OpenVP.SimplSharp.OpenVPBridge");
                bridge?.GetProperty("Slot")?.SetValue(null, _slot);
                // Library callbacks get a thread of their own, so the step debugger holding the logic
                // thread doesn't freeze the SIMPL+ modules that depend on them.
                _callbacks = new SlotDispatcher($"slot{_slot.Number:00}-simplsharp",
                    e => _slot.Log("Error", $"Unhandled exception in SIMPL# library callback: {e}"));
                bridge?.GetProperty("Callbacks")?.SetValue(null, _callbacks);
                _slot.Metrics.Callbacks = _callbacks;
            }
            var asm = _alc.LoadFromAssemblyName(new System.Reflection.AssemblyName(Path.GetFileNameWithoutExtension(dll)));
            var splusObject = _alc.LoadFromAssemblyName(new System.Reflection.AssemblyName("SplusObjects"))
                .GetType("Crestron.Logos.SplusObjects.SplusObject", throwOnError: true)!;
            var type = asm.GetTypes().FirstOrDefault(t => !t.IsAbstract && splusObject.IsAssignableFrom(t))
                ?? throw new InvalidDataException("no SIMPL+ module class");
            var ctor = type.GetConstructors().First(c => c.GetParameters().Length == 3);
            var encType = ctor.GetParameters()[2].ParameterType;
            instance = ctor.Invoke(new object[] { sym.Path, sym.Path, Enum.ToObject(encType, 1) });
            return new SplusNode(this, sym, (ISplusModule)instance);
        }
        catch (Exception e)
        {
            // A module that was created but can't run must release the program-wide start-up barrier it joined
            // in its constructor; otherwise every other module's WaitForInitializationComplete waits for it.
            try { (instance as ISplusModule)?.Stop(); } catch (Exception) { }
            var inner = e is System.Reflection.TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
            var unresolved = _alc?.Unresolved.Count > 0 ? $" (missing: {string.Join(", ", _alc.Unresolved.Distinct())})" : "";
            if (_moduleErrors.Add(dll!))
            {
                _slot.Log("Warning", $"SIMPL+ module {dll} can't run yet: {inner.GetType().Name}: {inner.Message}{unresolved}");
                _slot.ReportUnsupported("SIMPL+ module", Path.GetFileName(dll!), $"{inner.GetType().Name}: {inner.Message}{unresolved}", ProgramName);
            }
            return null;
        }
    }

    private void Register(Node node)
    {
        node.SymId = _symIds.TryGetValue(node.Symbol, out var id) ? id : node.Symbol.Path;
        _nodes.Add(node);
        for (int pin = 1; pin <= node.Symbol.Inputs.Length; pin++)
        {
            var s = node.Symbol.Inputs[pin - 1];
            // The constant 1 goes high at start: a node that wants that rising edge listens to it (see Start).
            if (s == 1 && node.WantsStartEdge(pin)) (_fanout![1] ??= new()).Add((node, pin));
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

    /// <summary>Whether a device, smart object or crosspoint input wired to <paramref name="sig"/> has a value to
    /// send when it connects. Real signals do. So does the constant 1 on a digital join: SIMPL Windows lets you
    /// type 1 on a pin to hold that join high (an item that is always visible or enabled), and the panel only
    /// knows if it is told. The constant 0 is the panel's default already.</summary>
    internal static bool HasFeedback(int sig, SigKind joinKind) => sig > 1 || (sig == 1 && joinKind == SigKind.Digital);

    // A signal only ever writes the array of its own kind, so reading another kind's array gives that
    // kind's default (false, 0, ""): same answers as the old "is true / is ushort / as string" checks.
    internal bool DigitalAt(int sig) => (uint)sig < (uint)_dig.Length && _dig[sig];
    internal ushort AnalogAt(int sig) => (uint)sig < (uint)_ana.Length ? _ana[sig] : (ushort)0;
    internal string SerialAt(int sig) => (uint)sig < (uint)_ser.Length ? _ser[sig] : string.Empty;

    internal SigKind KindOf(int sig) => sig >= 0 && sig < _kinds.Length ? _kinds[sig] : SigKind.Digital;

    // ------------------------------------------------------------------ graph

    /// <summary>Unique symbol ids: the record's path, or "#offset" when it has none; repeats get ".2", ".3"...</summary>
    private void AssignSymbolIds()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sym in _pkg.Logic.Symbols)
        {
            var id = sym.Path.Length > 0 ? sym.Path : $"#{sym.Offset}";
            var unique = id;
            for (int n = 2; !used.Add(unique); n++) unique = $"{id}.{n}";
            _symIds[sym] = unique;
        }
    }

    private string PinName(int sig) => sig == 0 ? "0" : sig == 1 ? "1" : NameOf(sig);

    public ProgramGraph GetGraph()
    {
        var symbols = new List<GraphSymbol>();
        var seen = new Dictionary<string, SigKind>(StringComparer.Ordinal);
        foreach (var sym in _pkg.Logic.Symbols)
        {
            if (!sym.Inputs.Concat(sym.Outputs).Any(x => x > 1)) continue; // structural records with nothing wired
            var id = _symIds[sym];
            var colon = sym.Path.IndexOf(':');
            var name = PrimitiveCatalog.Known.TryGetValue(sym.Type, out var info) ? info.Name : $"type {sym.Type}";
            var ins = sym.Inputs.Select(PinName).ToList();
            var outs = sym.Outputs.Select(PinName).ToList();
            foreach (var x in sym.Inputs.Concat(sym.Outputs)) seen.TryAdd(PinName(x), KindOf(x));
            symbols.Add(new GraphSymbol(id, name, sym.Path.Length > 0 ? sym.Path : id, colon > 0 ? sym.Path[..colon] : "", sym.IsDevice, ins, outs));
        }
        var signals = seen.Select(kv => new GraphSignal(kv.Key, kv.Value.ToString())).ToList();
        return new ProgramGraph(Kind, symbols, signals);
    }

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
        if (!_solving) Solve();
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
        if (!_solving) Solve();
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
        if (!_solving) Solve();
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
        if (!_solving) Solve();
    }

    private void Enqueue(int sig, uint num, string? str, string source)
    {
        // _dispOn is only set while tracing; everything else is queued plain and treated as external if traced.
        if (_dispOn) EnqueueTraced(sig, num, str, source);
        else _pending.Enqueue(new Pending(sig, num, str, source));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void EnqueueTraced(int sig, uint num, string? str, string source)
    {
        if (_metaCount >= 0xFFFF) { _pending.Enqueue(new Pending(sig, num, str, source)); return; } // too many to number: counts as external
        if (_metaCount == _meta.Length) Array.Resize(ref _meta, _meta.Length * 2);
        _meta[_metaCount] = new CauseInfo(_dispSym, _dispCause, _dispDepth + 1, _dispChain);
        _pending.Enqueue(new Pending(sig, num | ((uint)(++_metaCount) << 16), str, source));
    }

    private interface ITraceMode { static abstract bool On { get; } }
    private struct Plain : ITraceMode { public static bool On => false; }
    private struct Traced : ITraceMode { public static bool On => true; }

    private void Solve()
    {
        if (_solving) return;
        _solving = true;
        try
        {
            // Two copies of the loop, picked once per solve: the plain one has no tracing code in it at all,
            // so having a debugger feature costs nothing when none is open.
            if (_slot.TraceWanted) SolveLoop<Traced>(); else SolveLoop<Plain>();
        }
        finally { _solving = false; _dispOn = false; if (_pending.Count == 0) _metaCount = 0; }
    }

    private void SolveLoop<TMode>() where TMode : struct, ITraceMode
    {
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
                        var v = change.Value != 0;
                        var old = _dig[s];
                        if (old == v) continue;
                        _dig[s] = v;
                        if (TMode.On) { DispatchTraced(s, change, first, end, SigKind.Digital, od: old, nd: v); break; }
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
                        var v = (ushort)change.Value;
                        var old = _ana[s];
                        if (old == v) continue;
                        _ana[s] = v;
                        if (TMode.On) { DispatchTraced(s, change, first, end, SigKind.Analog, oa: old, na: v); break; }
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
                        if (TMode.On) { DispatchTraced(s, change, first, end, SigKind.Serial, os: old, ns: v); break; }
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
    }

    private void ReadMeta(in Pending change)
    {
        if (change.Meta >= 0)
        {
            var m = _meta[change.Meta];
            _chSym = m.Sym; _chCause = m.CauseSig; _chDepth = m.Depth; _chChain = m.Chain;
        }
        else { _chSym = null; _chCause = -1; _chDepth = 0; _chChain = ++_chainSeq; } // outside the logic: a new chain
    }

    private void TraceChange(int s, SigKind kind, object value, in Pending change)
    {
        ReadMeta(change);
        _slot.Trace(NameOf(s), kind, value, change.Source, _chSym,
            _chSym != null && _chCause >= 0 ? NameOf(_chCause) : null, _chDepth, _chChain);
    }

    /// <summary>The dispatch loop while a debugger listens: same as Solve's, plus causality bookkeeping. Kept out of
    /// Solve so the plain path stays small.</summary>
    private void DispatchTraced(int s, in Pending change, int first, int end, SigKind kind,
        bool od = false, bool nd = false, ushort oa = 0, ushort na = 0, string? os = null, string? ns = null)
    {
        TraceChange(s, kind, kind == SigKind.Digital ? SigValue.Box(nd) : kind == SigKind.Analog ? na : ns!, change);
        for (int e = first; e < end; e++)
        {
            var edge = _edges[e];
            Dispatching(edge.Node, s, change);
            try
            {
                switch (kind)
                {
                    case SigKind.Digital: edge.Node.OnDigital(edge.Pin, od, nd); break;
                    case SigKind.Analog: edge.Node.OnAnalog(edge.Pin, oa, na); break;
                    default: edge.Node.OnSerial(edge.Pin, os!, ns!); break;
                }
            }
            catch (Exception ex) { _slot.Log("Error", $"{edge.Node.Symbol}: {ex.Message}"); }
        }
        _dispOn = false;
    }

    private void Dispatching(Node node, int sig, in Pending change)
    {
        _dispOn = true;
        _dispSym = node.SymId;
        _dispCause = sig;
        _dispDepth = _chDepth;
        _dispChain = _chChain;
    }

    internal void Defer(Action a)
    {
        _nextWave.Enqueue(a);
        if (!_solving) _slot.Dispatcher.Post(static (rt, _, _) => ((LpzRuntime)rt!).Solve(), this, null, 0);
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
        foreach (var t in Unsupported.Keys.Where(t => t != -48))
            _slot.ReportUnsupported("Compiled primitive", $"type {t}",
                PrimitiveCatalog.Known.TryGetValue(t, out var info) ? $"{info.Name} {info.Notes}".Trim() : "unidentified", ProgramName);
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
        var sig = SignalNamed(name);
        if (sig < 0) return false;
        _slot.Dispatcher.Post(() => Drive(sig, value, "debugger"));
        return true;
    }

    public bool TryPulseSignal(string name)
    {
        var sig = SignalNamed(name);
        if (sig < 0 || _kinds[sig] != SigKind.Digital) return false;
        // One post, so nothing (a timer, a panel press) can run between the two edges.
        _slot.Dispatcher.Post(() =>
        {
            Drive(sig, true, "debugger");
            Drive(sig, false, "debugger");
        });
        return true;
    }

    /// <summary>The signal a debugger name refers to ("name" or "#index"), or -1.</summary>
    private int SignalNamed(string name)
    {
        int sig = -1;
        if (name.StartsWith('#') && int.TryParse(name[1..], out var n)) sig = n;
        else if (_pkg.Signals != null)
            sig = _pkg.Signals.Entries.Values.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Index ?? -1;
        return sig <= 1 || sig >= _kinds.Length ? -1 : sig;
    }

    public void Dispose()
    {
        Stop();
        _timers.Dispose();
        if (_slot.Metrics.Callbacks == _callbacks) _slot.Metrics.Callbacks = null;
        // A SIMPL# library can park a worker loop on this dispatcher (the Crestron driver SDK's DriverClock does) that
        // never returns, and nothing can stop it from here. Waiting the full 5 s for it made every unload of such a
        // program stall; a short wait still lets ordinary callbacks finish.
        _callbacks?.Dispose(TimeSpan.FromMilliseconds(250));
        _callbacks = null;
        _alc?.Unload();
        _alc = null;
    }

    // ------------------------------------------------------------------ nodes

    internal abstract class Node(LpzRuntime rt, BinSymbol sym)
    {
        public LpzRuntime Runtime { get; } = rt;
        public BinSymbol Symbol { get; } = sym;
        /// <summary>Graph symbol id (see <see cref="LpzRuntime.GetGraph"/>); set when the node is registered.</summary>
        public string SymId { get; set; } = string.Empty;
        public abstract SigKind? InputKind(int pin);
        public abstract SigKind? OutputKind(int pin);
        public virtual bool WatchesOutputs => false;
        /// <summary>Whether this node wants a rising edge on an input pin wired to the constant 1 at start-up
        /// (as a processor gives it). Nodes that already act on a high input in their Start say no.</summary>
        public virtual bool WantsStartEdge(int pin) => false;
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

    private partial class LogicNode(LpzRuntime rt, BinSymbol sym, Primitive prim) : Node(rt, sym), IPrimitiveContext
    {
        protected Primitive Prim => prim;
        public override SigKind? InputKind(int pin) => prim.InputKind(pin);
        public override SigKind? OutputKind(int pin) => prim.OutputKind(pin);
        public override bool WatchesOutputs => prim is IWatchesOutputs;
        public override bool WantsStartEdge(int pin) => prim.WantsStartEdge;
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

        public void SetInput(int pin, string value)
        {
            var sig = In(pin);
            if (sig > 1) Runtime.Drive(sig, value, Symbol.Path);
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
        public List<ExtenderNode> Extenders { get; } = new();

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
                if (HasFeedback(Symbol.Inputs[pin - 1], InJoin(pin).Kind)) Feedback(pin, Runtime.Value(Symbol.Inputs[pin - 1]));
            foreach (var so in SmartObjects.Values) so.PushAll();
            foreach (var ext in Extenders) ext.PushAll();
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
            if (pin < 1)
            {
                foreach (var ext in Extenders)
                    if (ext.FromPanel(key, value)) return;
                return;
            }
            var sig = Symbol.Outputs[pin - 1];
            if (sig > 1) Runtime._slot.Dispatcher.Post(static (n, v, s) => { var d = (DeviceNode)n!; d.Runtime.Drive(s, v!, d._source); }, this, value, sig);
        }
    }
}
