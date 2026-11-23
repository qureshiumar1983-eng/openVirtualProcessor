using System.Text.Json;

namespace OpenVP.Core;

/// <summary>
/// Everything about a slot's connections to the outside that OpenVP can watch or replace: the COM ports and sockets the program
/// opens, device simulators, network redirects and the IP ID remap. Settings live in <c>devices/</c> inside the slot's folder
/// (<c>simulators.json</c>, <c>redirects.json</c>, <c>ipremap.json</c>) and are part of the slot's snapshots.
/// </summary>
public sealed class SlotIo : IDisposable
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public SlotIo(ProgramSlot slot, ISimClock? clock = null)
    {
        Slot = slot;
        Ports = new PortHub(this);
        Sockets = new SocketTable();
        Simulators = new SlotSimulators(this, Path.Combine(Folder, "simulators.json"), clock);
        Redirects = new NetworkRedirects(Path.Combine(Folder, "redirects.json"));
        Redirects.SimResolver = Simulators.Endpoint;
        Remap = new IpIdRemap(Path.Combine(Folder, "ipremap.json"));
    }

    public ProgramSlot Slot { get; }
    /// <summary>The slot's <c>devices</c> folder.</summary>
    public string Folder => Path.Combine(Slot.Directory, "devices");
    public PortHub Ports { get; }
    public SocketTable Sockets { get; }
    public SlotSimulators Simulators { get; }
    public NetworkRedirects Redirects { get; }
    public IpIdRemap Remap { get; }

    /// <summary>Start the simulators that are enabled (at startup).</summary>
    public void Start() => Simulators.StartAll();

    /// <summary>The program was unloaded: its ports and sockets are gone. Simulators and redirects stay.</summary>
    public void ProgramUnloaded()
    {
        Ports.Clear();
        Sockets.CloseAll();
    }

    /// <summary>Read the settings from disk again (after a snapshot was restored).</summary>
    public void Reload()
    {
        Simulators.Load();
        Simulators.StartAll();
        Redirects.Load();
        Remap.Load();
    }

    public void Dispose() => Simulators.Dispose();
}
