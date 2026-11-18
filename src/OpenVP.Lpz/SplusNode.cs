using System.Reflection;
using System.Runtime.Loader;
using OpenVP.Core;

namespace OpenVP.Lpz;

/// <summary>
/// Load context for one compiled program's .NET code: its SIMPL+ modules and the SIMPL# libraries they use.
/// Crestron runtime names (SplusObjects, SplusLibrary, SmartThreadPoolCE, SimplSharp*) resolve to OpenVP's
/// clean-room implementations; Crestron's own copies shipped inside the .lpz are never loaded.
/// </summary>
internal sealed class LpzLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> Runtime = new(StringComparer.OrdinalIgnoreCase)
        { "SplusObjects", "SplusLibrary", "SmartThreadPoolCE", "OpenVP.Splus", "OpenVP.SimplSharp" };

    private readonly string _programDir;
    private readonly string _shimDir;

    public LpzLoadContext(int slot, string programDir, string shimDir) : base($"openvp-lpz-slot{slot:00}", isCollectible: true)
    {
        _programDir = programDir;
        _shimDir = shimDir;
    }

    public List<string> Unresolved { get; } = new();

    protected override Assembly? Load(AssemblyName name)
    {
        var simple = name.Name ?? "";
        if (simple == "OpenVP.Core") return null;
        // Compact Framework references (mscorlib 3.5, System, System.Core) bind to the running framework.
        if (simple is "mscorlib" or "System" or "System.Core" or "System.Xml" or "System.Data") return null;

        // Crestron runtime names OpenVP has a facade for go to OpenVP; other Crestron-shipped libraries
        // (SimplSharpNewtonsoft, Newtonsoft.Json.Compact, …) load from the program, like any library.
        var crestronName = Runtime.Contains(simple) || simple.StartsWith("SimplSharp", StringComparison.OrdinalIgnoreCase) ||
                           simple.StartsWith("Crestron.SimplSharp", StringComparison.OrdinalIgnoreCase);
        var shim = Path.Combine(_shimDir, simple + ".dll");
        if (crestronName && File.Exists(shim)) return LoadFromAssemblyPath(shim);
        var local = Path.Combine(_programDir, simple + ".dll");
        // Load the program's own assemblies from memory so the files aren't locked (Windows), which would
        // stop a recompiled program from replacing them on the next load.
        if (File.Exists(local)) return LoadFromStream(new MemoryStream(File.ReadAllBytes(local)));
        if (crestronName) { Unresolved.Add(simple); return null; }
        if (!simple.StartsWith("System", StringComparison.Ordinal) && simple != "netstandard") Unresolved.Add(simple);
        return null; // framework assemblies come from the default context
    }
}

/// <summary>
/// A compiled SIMPL+ module instance (record type −48). The record's inputs are the module's digital
/// inputs (ids 0…field10−1) followed by its analog/serial inputs (ids 0…); outputs likewise split at
/// field 11. Parameter id n is the record's n-th parameter (the first two are the module and .dll names).
/// </summary>
internal sealed class SplusNode : LpzRuntime.Node, ISplusHost
{
    private readonly int _digitalIns, _digitalOuts;
    private readonly ISplusModule _module;
    private readonly Dictionary<(bool, uint), SigKind> _pins;

    public SplusNode(LpzRuntime rt, BinSymbol sym, ISplusModule module) : base(rt, sym)
    {
        _module = module;
        _digitalIns = Math.Min(sym.FieldInt(10) ?? 0, sym.Inputs.Length);
        _digitalOuts = Math.Min(sym.FieldInt(11) ?? 0, sym.Outputs.Length);
        _module.AttachHost(this);
        _module.Initialize();
        _pins = new Dictionary<(bool, uint), SigKind>(_module.Pins);
        for (int i = 0; i < sym.Params.Count; i++)
        {
            var p = sym.Params[i];
            _module.SetParameter((uint)i, p.Text ?? (object)(p.Number ?? 0));
        }
    }

    public string DllName => Symbol.Params.Count > 1 ? Symbol.Params[1].Text ?? "" : "";

    private (SigKind kind, uint id)? InputOf(int pin)
    {
        if (pin <= _digitalIns) return (SigKind.Digital, (uint)(pin - 1));
        var id = (uint)(pin - 1 - _digitalIns);
        return _pins.TryGetValue((true, id), out var k) ? (k, id) : null;
    }

    private int OutputPin(SigKind kind, uint id) =>
        kind == SigKind.Digital ? (int)id + 1 : _digitalOuts + (int)id + 1;

    public override SigKind? InputKind(int pin) => InputOf(pin)?.kind;

    public override SigKind? OutputKind(int pin)
    {
        if (pin <= _digitalOuts) return SigKind.Digital;
        return _pins.TryGetValue((false, (uint)(pin - 1 - _digitalOuts)), out var k) ? k : null;
    }

    public override void Start()
    {
        for (int pin = 1; pin <= Symbol.Inputs.Length; pin++)
            if (InputOf(pin) is var (kind, id))
                _module.Preset(kind, id, Runtime.Value(Symbol.Inputs[pin - 1]));
        _module.StartMain();
    }

    public override void OnInput(int pin, object o, object n)
    {
        if (InputOf(pin) is var (kind, id)) _module.Input(kind, id, n);
    }

    public override void Stop() => _module.Stop();

    // ---------------- ISplusHost

    public void Output(SigKind kind, uint id, object value)
    {
        var pin = OutputPin(kind, id);
        if (pin < 1 || pin > Symbol.Outputs.Length) return;
        var sig = Symbol.Outputs[pin - 1];
        if (sig > 1) Runtime.Slot.Dispatcher.Post(static (n, v, s) => { var d = (SplusNode)n!; d.Runtime.Drive(s, v!, d.Symbol.Path); }, this, value, sig);
    }

    public bool IsConnected(bool isInput, SigKind kind, uint id)
    {
        int pin = isInput ? (kind == SigKind.Digital ? (int)id + 1 : _digitalIns + (int)id + 1) : OutputPin(kind, id);
        var arr = isInput ? Symbol.Inputs : Symbol.Outputs;
        return pin >= 1 && pin <= arr.Length && arr[pin - 1] > 1;
    }

    ProgramSlot ISplusHost.Slot => Runtime.Slot;

    public string FileRoot => Runtime.Slot.Directory;

    public void Log(string level, string message) => Runtime.Slot.Log(level, message);
}
