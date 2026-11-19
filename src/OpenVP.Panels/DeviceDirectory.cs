using OpenVP.Core;

namespace OpenVP.Panels;

/// <summary>
/// Finds the device a panel connection belongs to. If no program has registered the IP ID yet, a
/// placeholder is created in the requested slot; when the program later registers the same IP ID it
/// gets that same device, so a panel can connect before the program starts (as on real hardware).
/// </summary>
public static class DeviceDirectory
{
    /// <summary>
    /// Find or create the device for an IP ID. With <paramref name="roomId"/> the slot is the one that has
    /// that Room ID; returns null when no slot has it, so the caller can refuse the connection. Giving a
    /// slot too is fine if it is the same slot; if they disagree, throws ArgumentException.
    /// </summary>
    public static JoinDevice? Resolve(Processor processor, uint ipId, int? slot, string? roomId = null)
    {
        if (!string.IsNullOrWhiteSpace(roomId))
        {
            var bySlot = processor.FindSlotByRoomId(roomId);
            if (bySlot == null) return null;
            if (slot != null && slot != bySlot.Number)
                throw new ArgumentException($"Room ID '{roomId}' belongs to slot {bySlot.Number}, not slot {slot}.");
            slot = bySlot.Number;
        }
        var existing = processor.FindDevice(ipId, slot);
        if (existing != null) return existing;
        return processor.Slot(slot ?? 1).RegisterDevice(ipId, "Panel (waiting for program)", "Connected before registration");
    }
}
