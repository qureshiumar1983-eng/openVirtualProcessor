using System.Text;
using System.Text.Json;
using OpenVP.Core;
using OpenVP.Lpz;

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
        var compiled = CompanionLpz(path, slot);
        var problems = new List<string>();
        var overrides = compiled != null ? LogicSymbolCatalog.LoadMap(Path.ChangeExtension(compiled.Path, ".primitives.json"), problems) : null;
        foreach (var p in problems) slot.Log("Warning", p);
        return new SimplRuntime(slot, SmwFile.Load(path), lib, options, compiled, overrides);
    }

    /// <summary>
    /// The program's compiled .lpz, to run what the .smw can't (user macros, SIMPL+ modules): the .lpz of the same
    /// name next to the .smw, else the only .lpz in its folder. Signal names come from the .sig next to it, or from
    /// the archive inside it if that one agrees with the compile.
    /// </summary>
    private static LpzPackage? CompanionLpz(string smwPath, ProgramSlot slot)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(smwPath))!;
        var lpz = Path.ChangeExtension(smwPath, ".lpz");
        if (!File.Exists(lpz))
        {
            var all = Directory.GetFiles(dir, "*.lpz");
            if (all.Length != 1) return null;
            lpz = all[0];
        }
        try
        {
            var pkg = LpzPackage.Open(lpz);
            var sig = Path.ChangeExtension(lpz, ".sig");
            if (File.Exists(sig)) pkg.UseSignals(SigTable.Parse(File.ReadAllBytes(sig)));
            if (pkg.Signals != null && !SigCheck.Matches(pkg.Logic, pkg.Signals, out _)) pkg.UseSignals(null);
            return pkg;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            slot.Log("Warning", $"Couldn't read {Path.GetFileName(lpz)} for the program's macros and SIMPL+ modules: {e.Message}");
            return null;
        }
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
public sealed partial class SimplRuntime : IProgramRuntime, IProgramGraphSource
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
    private readonly Queue<Action> _nextWave = new();
    private readonly TimerSet _timers;
    private readonly NvramStore _store;
    private bool _solving;
    private volatile bool _running;
    // Causality (docs/debugger-paths.md section 2); only touched while a debugger is listening. While Solve hands
    // handle h to an edge's node, _dispOn is set and _disp* say who is acting and why, so an Enqueue made by
    // that node is stamped with its symbol id, h, depth + 1 and the chain.
    private bool _dispOn;
    private string? _dispSym;
    private int _dispCause;
    private int _dispDepth;
    private long _dispChain;
    private long _chainSeq;

    /// <param name="compiled">The program's compiled .lpz (with signal names), if there is one: symbols the .smw
    /// can't run on its own (user macros, SIMPL+ modules) run from it.</param>
    /// <param name="primitiveOverrides">Compiled types named in primitives.json, for those borrowed records.</param>
    public SimplRuntime(ProgramSlot slot, SmwFile file, SymbolLibrary library, SimplOptions options,
        LpzPackage? compiled = null, IReadOnlyDictionary<int, LogicSymbol>? primitiveOverrides = null)
    {
        _slot = slot;
        _file = file;
        _library = library;
        _options = options;
        // One OS timer for the whole slot; due timers reach the slot thread in one post (see TimerSet).
        _timers = new TimerSet(() => slot.Dispatcher, () => _running, e => slot.Log("Error", $"Unhandled exception in program: {e}"));
        _store = new NvramStore(Path.Combine(slot.Directory, "nvram.json"));
        foreach (var problem in library.Problems) slot.Log("Warning", problem);

        var max = 1;
        foreach (var h in file.Signals.Keys) max = Math.Max(max, h);
        foreach (var sym in file.Symbols.Values)
        {
            foreach (var h in sym.Inputs) max = Math.Max(max, h);
            foreach (var h in sym.Outputs) max = Math.Max(max, h);
        }
        if (primitiveOverrides != null) _primitiveOverrides = primitiveOverrides;
        if (compiled != null) PlanBorrowing(compiled, ref max);
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
        foreach (var h in _extraOnes) _constant[h] = true;

        Build();
        Array.Fill(_ser, string.Empty);
        Flatten();
    }

    // Arrays cost about 20 bytes per handle; real programs use thousands. This only stops a corrupt file from asking for gigabytes.
    private const int MaxHandle = 1 << 24;

    /// <summary>One listener of a signal: the node and which of its pins reads it.</summary>
    private readonly record struct Edge(Node Node, int Pin);

    /// <summary>A queued change. Digital and analog keep their value in Num, serial in Str.</summary>
    /// <remarks>Digital and analog values fit in 16 bits, so while tracing the high 16 bits of Num hold 1 + an index
    /// into _meta (causality); 0 means none. That keeps this struct at 24 bytes, which matters on the hot path.</remarks>
    private readonly record struct Pending(int Handle, uint Num, string? Str, string Source)
    {
        public uint Value => Num & 0xFFFF;
        public int Meta => (int)(Num >> 16) - 1;
    }

    private readonly record struct CauseInfo(string? Sym, int CauseHandle, int Depth, long Chain);
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
    public string ProgramIdTag => _file.ProgramIdTag;

    /// <summary>Symbols the interpreter has no behavior for (shown as warnings at load).</summary>
    public List<SmwSymbol> Unsupported { get; } = new();

    // ------------------------------------------------------------------ build

    private void Build()
    {
        var subDevices = new List<SmwSymbol>();
        var otherHardware = new List<SmwSymbol>();
        // The signal's own declared type (SgTp) decides first: n1I/n2I/n3I on a logic symbol are expansion groups, not kinds.
        foreach (var sg in _file.Signals.Values)
            if (sg.Handle > 0 && sg.Handle < _kinds.Length && sg.Type is 2 or 4) SetKind(sg.Handle, sg.Type == 2 ? SigKind.Analog : SigKind.Serial);
        foreach (var sym in _file.Symbols.Values.OrderBy(s => s.Handle))
        {
            if (sym.Disabled) continue; // switched off in SIMPL Windows: not part of the program
            if (BuildPort(sym)) continue;
            if (IsSystemControl(sym))
            {
                Register(new SystemControlNode(this, sym));
                continue;
            }
            if (IsDevice(sym))
            {
                var dev = new DeviceNode(this, sym);
                dev.Classify();
                _devices.Add(dev);
                Register(dev);
                // Hardware with nothing wired does nothing in the program: only the rest is worth reporting.
                if (!dev.Registered && (sym.Inputs.Any(h => h > 0) || sym.Outputs.Any(h => h > 0)))
                    (dev.Role == DeviceRole.SubDevice ? subDevices : otherHardware).Add(sym);
                continue;
            }

            if (BuildSpecial(sym)) continue;
            if (BuildBorrowed(sym)) continue;
            var logic = _library.Resolve(sym);
            if (logic == null)
            {
                // Structural symbols (processor, slots, folders) have no pins wired; only warn about the rest.
                if (sym.Inputs.Any(h => h > 0) || sym.Outputs.Any(h => h > 0))
                {
                    Unsupported.Add(sym);
                    _slot.ReportUnsupported("SIMPL symbol", $"{sym.Name} (SmC {sym.Code})",
                        $"pins in={sym.Inputs.Count} out={sym.Outputs.Count}", ProgramName);
                    _slot.Log("Warning", $"No behavior for symbol {sym} yet; its outputs stay at their defaults.");
                }
                continue;
            }
            if (!sym.Inputs.Any(h => h > 0) && !sym.Outputs.Any(h => h > 0)) continue; // nothing wired: nothing to run
            Register(new PrimNode(this, sym, logic));
        }

        LinkPanelChildren();
        UseCompiledKinds();
        ReportBorrowing();

        // Signals no pin gave a kind to are digital.
        foreach (var node in _nodes)
            foreach (var h in node.Symbol.Inputs.Concat(node.Symbol.Outputs))
                if (h > 0) SetKind(h, SigKind.Digital);
        foreach (var sg in _file.Signals.Values)
            if (sg.Handle > 0) SetKind(sg.Handle, SigKind.Digital);

        // Their pins keep their signals, but nothing drives or reads them: OpenVP only models the join space of an IP-ID device.
        foreach (var s in subDevices)
            _slot.ReportUnsupported("SIMPL sub-device", $"{s.Name} (SmC {s.Code})", "slot, port or extender inside an IP-ID device: not mapped to joins", ProgramName);
        foreach (var s in otherHardware)
            _slot.ReportUnsupported("SIMPL hardware", $"{s.Name} (SmC {s.Code})", "processor hardware (Cresnet, COM, IR, relays...)", ProgramName);
        if (subDevices.Count > 0)
            _slot.Log("Warning", $"{subDevices.Count} sub-device symbol(s) inside IP-ID devices (slots, ports, extenders) are not mapped to joins yet: " +
                string.Join(", ", subDevices.Select(s => s.Name).Distinct().Take(10)));
        if (otherHardware.Count > 0)
            _slot.Log("Warning", $"{otherHardware.Count} processor hardware symbol(s) (Cresnet, COM, IR, relays...) are not supported yet: " +
                string.Join(", ", otherHardware.Select(s => s.Name).Distinct().Take(10)));
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
            if (node.InputKind(pin) is { } k) SetKind(h, k);
            (_fanout![h] ??= new()).Add((node, pin));
        }
        for (int pin = 1; pin <= node.Symbol.Outputs.Count; pin++)
        {
            var h = node.Symbol.Outputs[pin - 1];
            if (h <= 0) continue;
            if (node.OutputKind(pin) is { } k) SetKind(h, k);
            if (node.WatchesOutputs) (_fanout![h] ??= new()).Add((node, -pin)); // negative pin = own output
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
            // As in compiled programs: a digital value (an input tied to 0 or 1) into a serial signal is empty text.
            default: Enqueue(handle, 0, string.Empty, source); break;
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

    private void Enqueue(int handle, uint num, string? str, string source)
    {
        // _dispOn is only set while tracing; everything else is queued plain and treated as external if traced.
        if (_dispOn) EnqueueTraced(handle, num, str, source);
        else _pending.Enqueue(new Pending(handle, num, str, source));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void EnqueueTraced(int handle, uint num, string? str, string source)
    {
        if (_metaCount >= 0xFFFF) { _pending.Enqueue(new Pending(handle, num, str, source)); return; } // too many to number: counts as external
        if (_metaCount == _meta.Length) Array.Resize(ref _meta, _meta.Length * 2);
        _meta[_metaCount] = new CauseInfo(_dispSym, _dispCause, _dispDepth + 1, _dispChain);
        _pending.Enqueue(new Pending(handle, num | ((uint)(++_metaCount) << 16), str, source));
    }

    /// <summary>Graph symbol id: "S" + the symbol's handle.</summary>
    internal static string SymbolId(SmwSymbol sym) => "S" + sym.Handle;

    private void ReadMeta(in Pending change)
    {
        if (change.Meta >= 0)
        {
            var m = _meta[change.Meta];
            _chSym = m.Sym; _chCause = m.CauseHandle; _chDepth = m.Depth; _chChain = m.Chain;
        }
        else { _chSym = null; _chCause = -1; _chDepth = 0; _chChain = ++_chainSeq; } // outside the logic: a new chain
    }

    private void TraceChange(int h, SigKind kind, object value, in Pending change)
    {
        ReadMeta(change);
        _slot.Trace(_file.SignalName(h), kind, value, change.Source, _chSym,
            _chSym != null && _chCause > 0 ? _file.SignalName(_chCause) : null, _chDepth, _chChain);
    }

    /// <summary>The dispatch loop while a debugger listens: same as Solve's, plus causality bookkeeping. Kept out of
    /// Solve so the plain path stays small.</summary>
    private void DispatchTraced(int h, in Pending change, int first, int end, SigKind kind,
        bool od = false, bool nd = false, ushort oa = 0, ushort na = 0, string? os = null, string? ns = null)
    {
        TraceChange(h, kind, kind == SigKind.Digital ? SigValue.Box(nd) : kind == SigKind.Analog ? na : ns!, change);
        for (int e = first; e < end; e++)
        {
            var edge = _edges[e];
            Dispatching(edge.Node, h, change);
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

    private void Dispatching(Node node, int handle, in Pending change)
    {
        _dispOn = true;
        _dispSym = SymbolId(node.Symbol);
        _dispCause = handle;
        _dispDepth = _chDepth;
        _dispChain = _chChain;
    }

    public ProgramGraph GetGraph()
    {
        var symbols = new List<GraphSymbol>();
        var seen = new Dictionary<string, SigKind>(StringComparer.Ordinal);
        string Pin(int h) => h > 0 ? _file.SignalName(h) : "";
        foreach (var sym in _file.Symbols.Values.OrderBy(s => s.Handle))
        {
            if (sym.Disabled || !sym.Inputs.Concat(sym.Outputs).Any(h => h > 0)) continue; // disabled, or structural with nothing wired
            foreach (var h in sym.Inputs.Concat(sym.Outputs))
                if (h > 0) seen.TryAdd(_file.SignalName(h), KindOf(h));
            var parent = _file.Symbols.GetValueOrDefault(sym.Parent);
            var id = SymbolId(sym);
            symbols.Add(new GraphSymbol(id, sym.Name, id, parent?.Name ?? "", IsDevice(sym),
                sym.Inputs.Select(Pin).ToList(), sym.Outputs.Select(Pin).ToList()));
        }
        return new ProgramGraph(Kind, symbols, seen.Select(kv => new GraphSignal(kv.Key, kv.Value.ToString())).ToList());
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
                    for (int i = 0; i < n; i++)
                    {
                        try { _nextWave.Dequeue()(); }
                        catch (Exception ex) { _slot.Log("Error", $"Next-wave action: {ex.Message}"); }
                    }
                    continue;
                }
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
                        var v = change.Value != 0;
                        var old = _dig[h];
                        if (old == v) continue;
                        _dig[h] = v;
                        if (TMode.On) { DispatchTraced(h, change, first, end, SigKind.Digital, od: old, nd: v); break; }
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
                        var old = _ana[h];
                        if (old == v) continue;
                        _ana[h] = v;
                        if (TMode.On) { DispatchTraced(h, change, first, end, SigKind.Analog, oa: old, na: v); break; }
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
                        if (TMode.On) { DispatchTraced(h, change, first, end, SigKind.Serial, os: old, ns: v); break; }
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

    internal IDisposable After(long ms, Action action)
    {
        return _timers.After(ms, action);
    }

    /// <summary>Run after the current logic solve settles (the next logic wave).</summary>
    internal void Defer(Action a)
    {
        _nextWave.Enqueue(a);
        if (!_solving) _slot.Dispatcher.Post(static (rt, _, _) => ((SimplRuntime)rt!).Solve(), this, null, 0);
    }

    // ------------------------------------------------------------------ lifecycle

    public void Start()
    {
        _slot.Dispatcher.Invoke(() =>
        {
            _running = true;
            var ones = _file.Signals.Values.Where(s => s.Name == "1" && s.Handle > 0 && s.Handle < _dig.Length).Select(s => s.Handle)
                .Concat(_extraOnes).ToList();
            foreach (var h in ones) _dig[h] = true; // the constant 1 reads high from the start, as on a processor
            foreach (var d in _devices) d.Attach();
            _solving = true; // collect start-up changes, then solve once
            try
            {
                foreach (var n in _nodes)
                {
                    try { n.Start(); }
                    catch (Exception e) { _slot.Log("Error", $"{n.Symbol}: {e.Message}"); }
                }

                // The "1" constant rises at start: devices, and symbols that want it, see a rising edge.
                foreach (var one in ones)
                {
                    if (_slot.TraceWanted) _slot.Trace("1", SigKind.Digital, true, "start");
                    for (int e = _edgeStart[one]; e < _edgeStart[one + 1]; e++)
                    {
                        var edge = _edges[e];
                        if (edge.Pin < 0 || !edge.Node.WantsStartEdge) continue;
                        try { edge.Node.OnDigital(edge.Pin, false, true); }
                        catch (Exception ex) { _slot.Log("Error", $"{edge.Node.Symbol}: {ex.Message}"); }
                    }
                }
            }
            finally { _solving = false; }
            Solve();
        });
        if (Unsupported.Count > 0)
            _slot.Log("Warning", $"{Unsupported.Count} symbol(s) have no behavior yet: " +
                string.Join(", ", Unsupported.Select(s => $"{s.Name} (SmC {s.Code})").Distinct().Take(20)));
    }

    public void Stop()
    {
        _running = false;
        foreach (var m in _borrowedModules)
            try { m.Stop(); } catch (Exception) { }
        _timers.CancelAll();
        foreach (var d in _devices) d.Detach();
        _store.Flush();
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
        _modules?.Dispose();
        _modules = null;
    }

    // ------------------------------------------------------------------ nodes

    internal abstract class Node
    {
        protected Node(SimplRuntime rt, SmwSymbol sym) { Runtime = rt; Symbol = sym; }
        public SimplRuntime Runtime { get; }
        public SmwSymbol Symbol { get; }
        /// <summary>The kind a pin implies for its signal, or null if the node doesn't say (the signal then takes
        /// its kind from another pin on it, or is digital).</summary>
        public abstract SigKind? InputKind(int pin);
        public abstract SigKind? OutputKind(int pin);
        /// <summary>Whether the node listens to its own outputs (negative pins on its edges).</summary>
        public virtual bool WatchesOutputs => false;
        /// <summary>Whether a pin wired to the constant 1 gets a rising edge at start-up.</summary>
        public virtual bool WantsStartEdge => true;
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

    /// <summary>
    /// A logic symbol, run by the same primitive the compiled-program engine uses (see LogicSymbolCatalog).
    /// Pins map one to one; an unconnected input reads as the constant the compiler would tie it to (0, or 1
    /// where the catalog says so). Parameters are converted to compiled form: times to 1/100 s, quoted text
    /// to strings with \x0D-style escapes decoded.
    /// </summary>
    private sealed class PrimNode : Node, IPrimitiveContext
    {
        private readonly Primitive _prim;
        private readonly LogicSymbol _logic;
        private int[] _in;                  // signal handle per input pin (the primitive's order); 0 = unconnected
        private readonly int[]? _fromSmw;   // .smw pin -> primitive pin (0: the primitive ignores it); null when they are the same
        private bool[] _unwiredHigh;
        private readonly BinSymbol _bin;
        private readonly (long? Number, string? Text)[] _params;
        private readonly string _source;

        public PrimNode(SimplRuntime rt, SmwSymbol sym, LogicSymbol logic) : base(rt, sym)
        {
            _logic = logic;
            _prim = logic.Create();
            _in = sym.Inputs.ToArray();
            _unwiredHigh = new bool[_in.Length + 1];
            for (int pin = 1; pin <= _in.Length; pin++) _unwiredHigh[pin] = _in[pin - 1] <= 0 && logic.UnwiredHigh(pin);
            if (logic.SmwPinOrder?.Invoke(_in.Length, sym.Outputs.Count) is { } order)
            {
                // The primitive takes the compiled pin order; this symbol's pins sit differently in the .smw.
                _fromSmw = new int[_in.Length + 1];
                var in2 = new int[order.Length];
                var high2 = new bool[order.Length + 1];
                for (int i = 0; i < order.Length; i++)
                {
                    if (order[i] < 1 || order[i] > _in.Length) continue;
                    in2[i] = _in[order[i] - 1];
                    high2[i + 1] = _unwiredHigh[order[i]];
                    _fromSmw[order[i]] = i + 1;
                }
                _in = in2;
                _unwiredHigh = high2;
            }
            // A compiled-style record for primitives that look at it: 0 and 1 are the constants, signal handles
            // are moved past them so they never collide.
            _bin = new BinSymbol
            {
                Type = logic.LpzTypes.Length > 0 ? logic.LpzTypes[0] : -1,
                Path = SymbolId(sym),
                Inputs = Enumerable.Range(1, _in.Length).Select(PinSignal).ToArray(),
                Outputs = sym.Outputs.Select(h => h > 0 ? h + 2 : 0).ToArray(),
            };
            _params = sym.Parameters.Select(ParseParam).ToArray();
            _source = sym.Name;
        }

        private int PinSignal(int pin)
        {
            var h = _in[pin - 1];
            if (h <= 0) return _unwiredHigh[pin] ? 1 : 0;
            if (Runtime._constant[h]) return Runtime._file.Signals.TryGetValue(h, out var sg) && sg.Name == "1" ? 1 : 0;
            return h + 2;
        }

        public override SigKind? InputKind(int pin) =>
            Symbol.HasGroups ? FromGroups(Symbol.InputGroups, pin) : _prim.InputKind(pin);
        public override SigKind? OutputKind(int pin) =>
            Symbol.HasGroups ? FromGroups(Symbol.OutputGroups, pin) : _prim.OutputKind(pin);
        public override bool WatchesOutputs => _prim is IWatchesOutputs;
        public override bool WantsStartEdge => _prim.WantsStartEdge;
        public override void Start() => _prim.Start(this);

        private int Pin(int smwPin) => _fromSmw == null ? smwPin : smwPin < _fromSmw.Length ? _fromSmw[smwPin] : 0;

        public override void OnInput(int pin, object o, object n)
        {
            if (pin < 0) ((IWatchesOutputs)_prim).OnOutputChanged(this, -pin, n);
            else if (Pin(pin) is var q and > 0) _prim.OnInput(this, q, o, n);
        }
        public override void OnDigital(int pin, bool o, bool n)
        {
            if (pin < 0) ((IWatchesOutputs)_prim).OnOutputChanged(this, -pin, SigValue.Box(n));
            else if (Pin(pin) is var q and > 0) _prim.OnDigital(this, q, o, n);
        }
        public override void OnAnalog(int pin, ushort o, ushort n)
        {
            if (pin < 0) ((IWatchesOutputs)_prim).OnOutputChanged(this, -pin, n);
            else if (Pin(pin) is var q and > 0) _prim.OnAnalog(this, q, o, n);
        }
        public override void OnSerial(int pin, string o, string n)
        {
            if (pin < 0) ((IWatchesOutputs)_prim).OnOutputChanged(this, -pin, n);
            else if (Pin(pin) is var q and > 0) _prim.OnSerial(this, q, o, n);
        }

        // ------------------------------------------------ IPrimitiveContext

        BinSymbol IPrimitiveContext.Symbol => _bin;
        public int InputCount => _in.Length;
        public int OutputCount => Symbol.Outputs.Count;
        private int In(int pin) => pin >= 1 && pin <= _in.Length ? _in[pin - 1] : 0;

        public bool Digital(int pin)
        {
            var h = In(pin);
            return h > 0 ? Runtime.DigitalAt(h) : pin >= 1 && pin <= _in.Length && _unwiredHigh[pin];
        }
        public ushort Analog(int pin) => Runtime.AnalogAt(In(pin));
        public string Serial(int pin) => Runtime.SerialAt(In(pin));
        public object Input(int pin)
        {
            var h = In(pin);
            return h > 0 ? Runtime.Value(h) : SigValue.Box(Digital(pin));
        }
        public bool IsConstant(int pin)
        {
            var h = In(pin);
            return h <= 0 || Runtime._constant[h];
        }

        public void Set(int pin, bool value)
        {
            if (pin >= 1 && pin <= Symbol.Outputs.Count) Runtime.Drive(Symbol.Outputs[pin - 1], value, _source);
        }
        public void Set(int pin, ushort value)
        {
            if (pin >= 1 && pin <= Symbol.Outputs.Count) Runtime.Drive(Symbol.Outputs[pin - 1], value, _source);
        }
        public void Set(int pin, string? value)
        {
            if (pin >= 1 && pin <= Symbol.Outputs.Count) Runtime.Drive(Symbol.Outputs[pin - 1], value, _source);
        }
        public void Set(int pin, object value)
        {
            if (pin >= 1 && pin <= Symbol.Outputs.Count) Runtime.Drive(Symbol.Outputs[pin - 1], value, _source);
        }

        public void SetFromInput(int pin, int inputPin)
        {
            var h = In(inputPin);
            if (h <= 0) { Set(pin, Digital(inputPin)); return; }
            switch (Runtime.KindOf(h))
            {
                case SigKind.Digital: Set(pin, Runtime.DigitalAt(h)); break;
                case SigKind.Analog: Set(pin, Runtime.AnalogAt(h)); break;
                default: Set(pin, Runtime.SerialAt(h)); break;
            }
        }

        public void SetInput(int pin, string value)
        {
            var h = In(pin);
            if (h > 0 && !Runtime._constant[h]) Runtime.Drive(h, value, _source);
        }

        public object Output(int pin) => pin >= 1 && pin <= Symbol.Outputs.Count ? Runtime.Value(Symbol.Outputs[pin - 1]) : SigValue.False;
        public SigKind OutputSignalKind(int pin) => pin >= 1 && pin <= Symbol.Outputs.Count ? Runtime.KindOf(Symbol.Outputs[pin - 1]) : SigKind.Digital;
        public int OutputSignal(int pin) => pin >= 1 && pin <= Symbol.Outputs.Count ? Symbol.Outputs[pin - 1] : 0;

        public int ParamCount => _params.Length;
        public long Number(int index, long fallback = 0) =>
            index >= 0 && index < _params.Length && _params[index].Number is long v ? v : fallback;
        public string? Text(int index) => index >= 0 && index < _params.Length ? _params[index].Text : null;

        public IDisposable AfterTicks(long ticks, Action action) => Runtime.After(ticks * 10, action);
        public void NextWave(Action action) => Runtime.Defer(action);
        public object? Recall(int pin) => Runtime._store.Get($"{_bin.Path}#{pin}");
        public void Remember(int pin, object value) => Runtime._store.Set($"{_bin.Path}#{pin}", value);
        public object? RecallKey(string key) => Runtime._store.Get(key);
        public void RememberKey(string key, object value) => Runtime._store.Set(key, value);
        public ProgramSlot Slot => Runtime._slot;

        public override string ToString() => $"{Symbol} as {_logic.Name}";
    }

    /// <summary>
    /// A .smw parameter in the form compiled programs carry it. Quoted text is a string (escapes decoded).
    /// Numbers keep their value (<c>10d</c>, <c>1Fh</c>, <c>50%</c> = 32768), except times, which become
    /// 1/100 s like every compiled time (<c>0.5s</c> = 50, <c>2m</c> = 12000, <c>250ms</c> = 25). Anything
    /// else is text.
    /// </summary>
    public static (long? Number, string? Text) ParseParam(string raw)
    {
        var s = raw.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') return (null, DecodeEscapes(s[1..^1]));
        if (s.Length == 0) return (null, null);
        if (SmwFile.TryParseNumber(s, out var v))
        {
            var last = char.ToLowerInvariant(s[^1]);
            var time = last is 's' or 'm' or 't'; // "ms" ends in 's' too
            return (time ? (long)Math.Round(v / 10.0) : v, null);
        }
        return (null, DecodeEscapes(s));
    }

    /// <summary>SIMPL string escapes: \xHH, \r, \n, \t, \\ and \".</summary>
    public static string DecodeEscapes(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch != '\\' || i + 1 >= s.Length) { sb.Append(ch); continue; }
            var next = s[i + 1];
            switch (char.ToLowerInvariant(next))
            {
                case 'x' when i + 3 < s.Length && IsHex(s[i + 2]) && IsHex(s[i + 3]):
                    sb.Append((char)Convert.ToInt32(s.Substring(i + 2, 2), 16)); i += 3; break;
                case 'r': sb.Append('\r'); i++; break;
                case 'n': sb.Append('\n'); i++; break;
                case 't': sb.Append('\t'); i++; break;
                case '\\': sb.Append('\\'); i++; break;
                case '"': sb.Append('"'); i++; break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();

        static bool IsHex(char c) => Uri.IsHexDigit(c);
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

        public JoinDevice? Device => _device;
        /// <summary>Smart Graphics objects on this panel, by object ID.</summary>
        public Dictionary<uint, SmartObjectNode> SmartObjects { get; } = new();
        /// <summary>Reserved joins, toolbars and app control: joins at fixed bases on this device.</summary>
        public List<ExtenderNode> Extenders { get; } = new();

        public override SigKind? InputKind(int pin) => FromGroups(Symbol.InputGroups, pin);
        public override SigKind? OutputKind(int pin) => FromGroups(Symbol.OutputGroups, pin);

        private bool IsEisc =>
            Symbol.Name.Contains("Intersystem", StringComparison.OrdinalIgnoreCase) ||
            Symbol.Name.Contains("EISC", StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether this symbol gets a device in the IP table: IP-ID devices do, sub-devices and other hardware don't.</summary>
        public bool Registered { get; private set; } = true;

        public DeviceRole Role { get; private set; } = DeviceRole.IpId;

        public void Classify()
        {
            Role = Runtime._file.RoleOf(Symbol.DeviceHandle);
            // An explicit "ipids" entry in openvp.json always wins.
            Registered = Role == DeviceRole.IpId || Runtime._options.Ipids.ContainsKey(Symbol.Handle.ToString());
        }

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
                return (uint)(0x03 + Runtime._devices.Where(d => d.Registered).ToList().IndexOf(this));
            }
        }

        public void Attach()
        {
            if (!Registered) return;
            var slot = Runtime._slot;
            _device = slot.RegisterDevice(IpId, IsEisc ? "EISC" : Symbol.Name, $"SIMPL symbol #{Symbol.Handle}");
            _source = $"IPID{_device.IpId:X2}";
            _device.InputReceived += OnDeviceInput;
            if (IsEisc) slot.Processor.Bus.Attach(_device);
            // Push current feedback values so a panel that connects later sees them.
            for (int pin = 1; pin <= Symbol.Inputs.Count; pin++)
                if (Symbol.Inputs[pin - 1] > 0) Feedback(pin, Runtime.Value(Symbol.Inputs[pin - 1]));
            foreach (var so in SmartObjects.Values) so.PushAll();
            foreach (var ext in Extenders) ext.PushAll();
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
            if (key.SmartObject != 0)
            {
                if (SmartObjects.TryGetValue(key.SmartObject, out var so)) so.FromPanel(key, value);
                return;
            }
            foreach (var ext in Extenders)
                if (ext.FromPanel(key, value)) return;
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
