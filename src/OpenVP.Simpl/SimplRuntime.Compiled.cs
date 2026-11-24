using OpenVP.Core;
using OpenVP.Lpz;

namespace OpenVP.Simpl;

// Symbols the .smw can't run on its own (user macros, SIMPL+ modules, primitives OpenVP doesn't know by name)
// borrowed from the program's compiled .lpz. A .smw symbol's compiled form sits at its position in the symbol
// tree below the Logic folder (S-3.2.1 = Logic's 3rd child, its 2nd child, that one's 1st child): a SIMPL+
// module or a primitive is the record at that path, and a macro is every record under "path:", flattened by the
// compiler. Signals at the macro's boundary carry the same names in the .sig and the .smw, so they map by name;
// signals inside the macro (named "::NAME:path") get handles of their own. A scope is only used if every
// program-level signal it touches is wired to the symbol, so a .smw edited after the compile never picks up
// the wrong logic: that symbol just stays unsupported.
public sealed partial class SimplRuntime
{
    /// <summary>A .smw symbol's compiled records, with their signals in this runtime's handles.</summary>
    private sealed record Borrowed(List<BinSymbol> Records);

    private readonly Dictionary<int, Borrowed> _borrowed = new();
    private readonly List<int> _extraOnes = new();
    private readonly Dictionary<int, string> _borrowedNames = new();
    private readonly List<SplusModuleHost> _borrowedModules = new();
    private readonly HashSet<string> _borrowedModuleErrors = new();
    private LpzPackage? _compiled;
    private IReadOnlyDictionary<int, LogicSymbol> _primitiveOverrides = new Dictionary<int, LogicSymbol>();
    private SplusModuleFactory? _modules;
    private int _oneHandle = 1;

    /// <summary>How many .smw symbols run from the compiled program, and how many records that is.</summary>
    public (int Symbols, int Records) BorrowedCount => (_borrowed.Count, _borrowed.Values.Sum(b => b.Records.Count));

    /// <summary>Symbols that always run natively: devices and what hangs off them, ports and crosspoints. Every other
    /// symbol, logic primitives included, runs from its compiled record when there is one: the compiler's pin order
    /// and parameter units are then exactly what a processor runs (the .smw's own pin order differs from the
    /// compiled one for several symbols, e.g. Multiple One Shots, Set/Reset Latch, Toggle).</summary>
    private bool RunsNatively(SmwSymbol s) =>
        IsDevice(s) || s.Code is EquipmentCrosspoint or ControlCrosspoint or CrosspointConnect or SmartObjectCode ||
        s.Raw.Get("CedH") != null || (s.DeviceHandle > 0 && (IsComPort(s) || IsIrPort(s))) ||
        (s.HasGroups && ExtenderJoins.For(s.Code, s.Name) != null);

    private bool CanRun(BinSymbol r) => r.Type is -48 or 213 or 214 or 215 || r.Type >= 0 && PrimitiveLibrary.Create(r.Type, _primitiveOverrides) != null;

    /// <summary>Compiled crosspoint records (inside macros) run as the .smw crosspoint symbols: pin groups from fields
    /// 10-13, the crosspoint ID from the first numeric parameter. False for any other record.</summary>
    private bool BuildCompiledCrosspoint(SmwSymbol shadow, BinSymbol r)
    {
        if (r.Type is not (213 or 214 or 215)) return false;
        int inD = Math.Min(r.FieldInt(10) ?? r.Inputs.Length, r.Inputs.Length), inA = Math.Clamp(r.FieldInt(12) ?? inD, inD, r.Inputs.Length);
        int outD = Math.Min(r.FieldInt(11) ?? r.Outputs.Length, r.Outputs.Length), outA = Math.Clamp(r.FieldInt(13) ?? outD, outD, r.Outputs.Length);
        shadow.InputGroups[0] = inD; shadow.InputGroups[1] = inA - inD; shadow.InputGroups[2] = r.Inputs.Length - inA;
        shadow.OutputGroups[0] = outD; shadow.OutputGroups[1] = outA - outD; shadow.OutputGroups[2] = r.Outputs.Length - outA;
        var id = r.Params.Where(p => p.Number != null).Select(p => p.Number!.Value).FirstOrDefault();
        shadow.Parameters.Add($"{id}d");
        switch (r.Type)
        {
            case 213: var eq = new XEquipmentNode(this, shadow); _xEquipment[eq.Id] = eq; Register(eq); break;
            case 214: var ctl = new XControlNode(this, shadow); _xControls[ctl.Id] = ctl; Register(ctl); break;
            default: Register(new XConnectNode(this, shadow)); break;
        }
        return true;
    }

    /// <summary>Works out which symbols come from the compiled program and gives the signals inside them handles
    /// after <paramref name="max"/>. Runs before the signal arrays exist.</summary>
    private void PlanBorrowing(LpzPackage pkg, ref int max)
    {
        if (pkg.Signals is not { } sig) { _slot.Log("Warning", "The compiled program has no signal names (.sig), so its macros and SIMPL+ modules can't be matched to the .smw."); return; }
        if (CompiledPairing.LogicRoot(_file) == null) return;

        var paths = CompiledPairing.SymbolPaths(_file);
        var scopes = CompiledPairing.Scopes(pkg);

        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var sg in _file.Signals.Values)
            if (sg.Handle > 0 && sg.Name.Length > 0) byName.TryAdd(sg.Name, sg.Handle);
        var zero = _file.Signals.Values.FirstOrDefault(s => s.Name == "0")?.Handle ?? 0;
        var one = _file.Signals.Values.FirstOrDefault(s => s.Name == "1")?.Handle ?? 0;
        var local = new Dictionary<int, int>();
        int next = max;

        int Map(int lpzSig)
        {
            if (lpzSig == 0) return zero;
            if (lpzSig == 1)
            {
                if (one == 0) { one = ++next; _extraOnes.Add(one); }
                return one;
            }
            var name = sig.NameOf(lpzSig);
            if (name != null && !name.StartsWith("::", StringComparison.Ordinal) && byName.TryGetValue(name, out var h)) return h;
            if (!local.TryGetValue(lpzSig, out h))
            {
                local[lpzSig] = h = ++next;
                if (name != null) _borrowedNames[h] = name;
            }
            return h;
        }

        int rejected = 0;
        foreach (var s in _file.Symbols.Values)
        {
            if (s.Disabled || !s.Inputs.Any(h => h > 0) && !s.Outputs.Any(h => h > 0)) continue;
            if (RunsNatively(s) || !paths.TryGetValue(s.Handle, out var path) || !scopes.TryGetValue(path, out var records)) continue;
            // Every program-level signal the scope touches must be wired to this symbol.
            var wired = new HashSet<string>(s.Inputs.Concat(s.Outputs).Where(h => h > 0 && _file.Signals.ContainsKey(h))
                .Select(h => _file.Signals[h].Name), StringComparer.OrdinalIgnoreCase);
            var global = records.SelectMany(r => r.Inputs.Concat(r.Outputs)).Where(x => x > 1)
                .Select(x => sig.NameOf(x)).Where(n => n != null && !n.StartsWith("::", StringComparison.Ordinal)).Distinct().ToList();
            if (global.Count == 0 && wired.Count > 0 || global.Any(n => !wired.Contains(n!))) { rejected++; continue; }
            var mapped = records.Select(r =>
            {
                var b = new BinSymbol { Offset = r.Offset, Type = r.Type, Path = r.Path, Inputs = r.Inputs.Select(Map).ToArray(), Outputs = r.Outputs.Select(Map).ToArray() };
                b.Fields.AddRange(r.Fields);
                b.Params.AddRange(r.Params);
                return b;
            }).ToList();
            _borrowed[s.Handle] = new Borrowed(mapped);
        }
        max = next;
        _oneHandle = one;
        _compiled = pkg;
        _borrowRejected = rejected;
    }

    private int _borrowRejected;

    /// <summary>After build: say how much runs from the compiled program.</summary>
    private void ReportBorrowing()
    {
        if (_compiled == null) return;
        _slot.Log("Notice", $"Compiled program {Path.GetFileName(_compiled.Path)}: {_borrowed.Count} symbol(s) run from it, macros and SIMPL+ modules included" +
                            (_borrowRejected > 0 ? $"; {_borrowRejected} don't match it (the .smw changed after that compile?) and run natively or stay unsupported." : "."));
        if (_compiled.Signals == null) return;
        try
        {
            var wiring = WiringCheck.Analyze(_file, _compiled, null, _primitiveOverrides);
            if (wiring.TypesDisagree > 0)
                _slot.Log("Warning", $"Wiring check: {wiring.TypesDisagree} symbol type{(wiring.TypesDisagree == 1 ? "'s" : "s'")} pin order differs from OpenVP's model; see Compatibility.");
        }
        catch (Exception e) when (e is InvalidOperationException or InvalidDataException or KeyNotFoundException) { /* the check is advisory */ }
    }

    /// <summary>The compiler wrote every signal's kind into the .sig; with one at hand those beat the kinds guessed
    /// from pins, for the .smw's own signals as well as the ones inside borrowed macros.</summary>
    private void UseCompiledKinds()
    {
        if (_compiled?.Signals is not { } sig) return;
        var byName = new Dictionary<string, SigEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in sig.Entries.Values) byName.TryAdd(e.Name, e);
        void Force(int h, SigEntry e)
        {
            if (h <= 0 || h >= _kinds.Length || _constant[h]) return;
            var k = ((int)e.Kind & 3) switch { 1 => SigKind.Analog, 2 => SigKind.Serial, _ => SigKind.Digital };
            _kinds[h] = k;
            _kindKnown[h] = true;
        }
        foreach (var sg in _file.Signals.Values)
            if (byName.TryGetValue(sg.Name, out var e)) Force(sg.Handle, e);
        foreach (var (h, name) in _borrowedNames)
            if (byName.TryGetValue(name, out var e)) Force(h, e);
    }

    /// <summary>Registers a borrowed symbol's records. False if the symbol isn't borrowed.</summary>
    private bool BuildBorrowed(SmwSymbol sym)
    {
        if (!_borrowed.TryGetValue(sym.Handle, out var b)) return false;
        // A symbol OpenVP runs natively keeps doing so if part of its compiled form can't run here.
        if (_library.Resolve(sym) != null && !b.Records.All(CanRun)) { _borrowed.Remove(sym.Handle); return false; }
        foreach (var r in b.Records)
        {
            var shadow = new SmwSymbol { Handle = sym.Handle, Code = sym.Code, Name = $"{sym.Name.Trim()} › {r.Path}", Parent = sym.Parent };
            shadow.Inputs.AddRange(r.Inputs);
            shadow.Outputs.AddRange(r.Outputs);
            if (r.Type == -48)
            {
                var dll = r.Params.Count > 1 ? r.Params[1].Text : null;
                if (string.IsNullOrEmpty(dll)) continue;
                _modules ??= new SplusModuleFactory(_slot, _compiled!);
                var module = _modules.Create(dll, r.Path, out var error);
                if (module == null)
                {
                    if (_borrowedModuleErrors.Add(dll))
                    {
                        _slot.Log("Warning", $"SIMPL+ module {dll} (in {sym.Name.Trim()}) can't run yet: {error}");
                        _slot.ReportUnsupported("SIMPL+ module", Path.GetFileName(dll), error, ProgramName);
                    }
                    continue;
                }
                var node = new CompiledSplusNode(this, shadow, r, module, _oneHandle);
                _borrowedModules.Add(node.Host);
                Register(node);
                continue;
            }
            if (BuildCompiledCrosspoint(shadow, r)) continue;
            var prim = r.Type >= 0 ? PrimitiveLibrary.Create(r.Type, _primitiveOverrides) : null;
            if (prim == null)
            {
                var what = PrimitiveCatalog.Known.TryGetValue(r.Type, out var info) ? info.Name : "unidentified";
                _slot.ReportUnsupported("Compiled primitive", $"type {r.Type}", $"{what}; inside {sym.Name.Trim()} (run from the .lpz)", ProgramName);
                continue;
            }
            Register(new CompiledNode(this, shadow, r, prim));
        }
        return true;
    }

    /// <summary>A compiled logic record running in this runtime: pins are already this runtime's handles.</summary>
    private sealed class CompiledNode : Node, IPrimitiveContext
    {
        private readonly Primitive _prim;
        private readonly BinSymbol _bin;
        private readonly string _source;

        public CompiledNode(SimplRuntime rt, SmwSymbol shadow, BinSymbol bin, Primitive prim) : base(rt, shadow)
        {
            _bin = bin;
            _prim = prim;
            _source = shadow.Name;
        }

        public override SigKind? InputKind(int pin) => _prim.InputKind(pin);
        public override SigKind? OutputKind(int pin) => _prim.OutputKind(pin);
        public override bool WatchesOutputs => _prim is IWatchesOutputs;
        public override bool WantsStartEdge => _prim.WantsStartEdge;
        public override void Start() => _prim.Start(this);

        public override void OnInput(int pin, object o, object n)
        {
            if (pin < 0) ((IWatchesOutputs)_prim).OnOutputChanged(this, -pin, n);
            else _prim.OnInput(this, pin, o, n);
        }
        public override void OnDigital(int pin, bool o, bool n)
        {
            if (pin < 0) ((IWatchesOutputs)_prim).OnOutputChanged(this, -pin, SigValue.Box(n));
            else _prim.OnDigital(this, pin, o, n);
        }
        public override void OnAnalog(int pin, ushort o, ushort n)
        {
            if (pin < 0) ((IWatchesOutputs)_prim).OnOutputChanged(this, -pin, n);
            else _prim.OnAnalog(this, pin, o, n);
        }
        public override void OnSerial(int pin, string o, string n)
        {
            if (pin < 0) ((IWatchesOutputs)_prim).OnOutputChanged(this, -pin, n);
            else _prim.OnSerial(this, pin, o, n);
        }

        // ------------------------------------------------ IPrimitiveContext

        BinSymbol IPrimitiveContext.Symbol => _bin;
        public int InputCount => _bin.Inputs.Length;
        public int OutputCount => _bin.Outputs.Length;
        private int In(int pin) => pin >= 1 && pin <= _bin.Inputs.Length ? _bin.Inputs[pin - 1] : 0;
        private int Out(int pin) => pin >= 1 && pin <= _bin.Outputs.Length ? _bin.Outputs[pin - 1] : 0;

        public bool Digital(int pin) => Runtime.DigitalAt(In(pin));
        public ushort Analog(int pin) => Runtime.AnalogAt(In(pin));
        public string Serial(int pin) => Runtime.SerialAt(In(pin));
        public object Input(int pin) => In(pin) is var h and > 0 ? Runtime.Value(h) : SigValue.False;
        public bool IsConstant(int pin) => In(pin) is var h && (h <= 0 || Runtime._constant[h]);

        public void Set(int pin, bool value) => Runtime.Drive(Out(pin), value, _source);
        public void Set(int pin, ushort value) => Runtime.Drive(Out(pin), value, _source);
        public void Set(int pin, string? value) => Runtime.Drive(Out(pin), value, _source);
        public void Set(int pin, object value) => Runtime.Drive(Out(pin), value, _source);

        public void SetFromInput(int pin, int inputPin)
        {
            var h = In(inputPin);
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

        public object Output(int pin) => Out(pin) is var h and > 0 ? Runtime.Value(h) : SigValue.False;
        public SigKind OutputSignalKind(int pin) => Runtime.KindOf(Out(pin));
        public int OutputSignal(int pin) => Out(pin);

        public int ParamCount => _bin.Params.Count;
        public long Number(int index, long fallback = 0) =>
            index >= 0 && index < _bin.Params.Count && _bin.Params[index].Number is int v ? v : fallback;
        public string? Text(int index) => index >= 0 && index < _bin.Params.Count ? _bin.Params[index].Text : null;

        public IDisposable AfterTicks(long ticks, Action action) => Runtime.After(ticks * 10, action);
        public void NextWave(Action action) => Runtime.Defer(action);
        public object? Recall(int pin) => Runtime._store.Get($"{_bin.Path}#{pin}");
        public void Remember(int pin, object value) => Runtime._store.Set($"{_bin.Path}#{pin}", value);
        public object? RecallKey(string key) => Runtime._store.Get(key);
        public void RememberKey(string key, object value) => Runtime._store.Set(key, value);
        public ProgramSlot Slot => Runtime._slot;

        public override string ToString() => Symbol.Name;
    }

    /// <summary>A compiled SIMPL+ module (record type −48) from the .lpz, running in this runtime.</summary>
    private sealed class CompiledSplusNode : Node
    {
        public SplusModuleHost Host { get; }

        public CompiledSplusNode(SimplRuntime rt, SmwSymbol shadow, BinSymbol bin, ISplusModule module, int one) : base(rt, shadow)
        {
            Host = new SplusModuleHost(new Engine(rt), bin, module, one);
        }

        private sealed class Engine(SimplRuntime rt) : ISplusEngine
        {
            public void Drive(int sig, object value, string source) => rt.Drive(sig, value, source);
            public object Value(int sig) => rt.Value(sig);
            public ProgramSlot Slot => rt._slot;
        }

        public override SigKind? InputKind(int pin) => Host.InputKind(pin);
        public override SigKind? OutputKind(int pin) => Host.OutputKind(pin);
        // Digital inputs get the start-up edge of the constant 1, as in compiled programs (SplusModuleHost.Start
        // presets them low for that).
        public override bool WantsStartEdge => true;
        public override void Start() => Host.Start();
        public override void OnInput(int pin, object o, object n)
        {
            // The start-up edge of the constant 1 reaches every pin wired to it; only digital inputs take it.
            if (n is bool && Host.InputKind(pin) != SigKind.Digital) return;
            Host.OnInput(pin, n);
        }
    }
}
