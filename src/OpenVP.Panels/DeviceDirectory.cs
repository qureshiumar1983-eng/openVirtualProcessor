using OpenVP.Core;

namespace OpenVP.Panels;

/// <summary>
/// Finds the device a panel connection belongs to. If no program has registered the IP ID yet, a
/// placeholder is created in the requested slot; when the program later registers the same IP ID it
/// gets that same device, so a panel can connect before the program starts (as on real hardware).
/// </summary>
public static class DeviceDirectory
{
    public static JoinDevice Resolve(Processor processor, uint ipId, int? slot)
    {
        var existing = processor.FindDevice(ipId, slot);
        if (existing != null) return existing;
        return processor.Slot(slot ?? 1).RegisterDevice(ipId, "Panel (waiting for program)", "Connected before registration");
    }
}
