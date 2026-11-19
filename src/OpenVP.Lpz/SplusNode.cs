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

/// <summary>A compiled SIMPL+ module instance (record type −48) in a compiled program; see <see cref="SplusModuleHost"/>.</summary>
internal sealed class SplusNode : LpzRuntime.Node
{
    private readonly SplusModuleHost _host;

    public SplusNode(LpzRuntime rt, BinSymbol sym, ISplusModule module) : base(rt, sym)
    {
        _host = new SplusModuleHost(new Engine(rt), sym, module);
    }

    private sealed class Engine(LpzRuntime rt) : ISplusEngine
    {
        public void Drive(int sig, object value, string source) => rt.Drive(sig, value, source);
        public object Value(int sig) => rt.Value(sig);
        public ProgramSlot Slot => rt.Slot;
    }

    public string DllName => _host.DllName;
    public override SigKind? InputKind(int pin) => _host.InputKind(pin);
    public override SigKind? OutputKind(int pin) => _host.OutputKind(pin);
    public override bool WantsStartEdge(int pin) => _host.WantsStartEdge(pin);
    public override void Start() => _host.Start();
    public override void OnInput(int pin, object o, object n) => _host.OnInput(pin, n);
    public override void Stop() => _host.Stop();
}
