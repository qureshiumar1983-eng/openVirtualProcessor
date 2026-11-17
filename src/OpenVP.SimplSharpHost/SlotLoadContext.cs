using System.Reflection;
using System.Runtime.Loader;

namespace OpenVP.SimplSharpHost;

/// <summary>
/// One load context per SIMPL# slot. The program, its own dependencies, the API shim and the facades
/// all load here, so each slot gets its own statics and can be unloaded on stop. OpenVP.Core is shared
/// with the host so devices and the IP table are visible to the panel servers.
/// </summary>
internal sealed class SlotLoadContext : AssemblyLoadContext
{
    private static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase) { "OpenVP.Core" };

    private readonly string _programDir;
    private readonly string _shimDir;

    public SlotLoadContext(int slot, string programDir, string shimDir)
        : base($"openvp-slot{slot:00}", isCollectible: true)
    {
        _programDir = programDir;
        _shimDir = shimDir;
    }

    /// <summary>Crestron assembly names a program asked for that no facade covered.</summary>
    public List<string> Unresolved { get; } = new();

    protected override Assembly? Load(AssemblyName name)
    {
        var simple = name.Name ?? string.Empty;
        if (Shared.Contains(simple)) return null; // use the host's copy

        // The shim and facades win over anything the program shipped (a .cpz may include Crestron's DLLs).
        var shimPath = Path.Combine(_shimDir, simple + ".dll");
        if (IsShimAssembly(simple) && File.Exists(shimPath)) return LoadFromAssemblyPath(shimPath);

        var programPath = Path.Combine(_programDir, simple + ".dll");
        if (File.Exists(programPath)) return LoadFromAssemblyPath(programPath);

        if (LooksLikeCrestron(simple)) Unresolved.Add(simple);
        return null; // framework assemblies come from the default context
    }

    private bool IsShimAssembly(string simple) =>
        simple == "OpenVP.SimplSharp" || LooksLikeCrestron(simple);

    internal static bool LooksLikeCrestron(string simple) =>
        simple.StartsWith("SimplSharp", StringComparison.OrdinalIgnoreCase) ||
        simple.StartsWith("Crestron", StringComparison.OrdinalIgnoreCase);
}
