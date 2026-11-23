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
        // ipId is the number the panel connects to. A slot's IP ID remap may send it to another of the program's IDs.
        var existing = processor.FindPanelDevice(ipId, slot);
        if (existing != null) return existing;
        var target = slot is int s ? processor.Slot(s) : processor.SlotRemapping(ipId) ?? processor.Slots.FirstOrDefault(x => x.Io.Remap.IsRemapped(ipId)) ?? processor.Slot(1);
        if (target.Io.Remap.ToProgram(ipId) is not { } programId)
        {
            // The program's device with this number was remapped to another number, so nothing answers here any more.
            // The panel gets a device that belongs to no slot, so it can connect but drives nothing.
            var moved = target.Io.Remap.PublicOf(ipId);
            return new JoinDevice(target.Number, ipId, "Panel (IP ID remapped)", $"IP ID {ipId:X2} was remapped; this program answers at {moved:X2}");
        }
        return target.RegisterDevice(programId, "Panel (waiting for program)", "Connected before registration");
    }
}
