using OpenVP.Core;
using OpenVP.Panels;

namespace OpenVP.Tests;

/// <summary>Per-slot Room ID: default, validation, persistence, uniqueness, routing.</summary>
public static class RoomIdTests
{
    private static void Throws<T>(Action a, string what) where T : Exception
    {
        try { a(); }
        catch (T) { return; }
        throw new AssertException($"{what}: expected {typeof(T).Name}");
    }

    [Test]
    public static void Room_id_defaults_to_slot_number()
    {
        using var p = new Processor(Paths.TempData(), 3);
        Assert.Equal("SLOT01", p.Slot(1).RoomId, "slot 1");
        Assert.Equal("SLOT03", p.Slot(3).RoomId, "slot 3");
    }

    [Test]
    public static void Room_id_is_validated_and_trimmed()
    {
        using var p = new Processor(Paths.TempData(), 2);
        p.Slot(1).SetRoomId("  Room_2.a-b  ");
        Assert.Equal("Room_2.a-b", p.Slot(1).RoomId, "trimmed");
        Throws<ArgumentException>(() => p.Slot(1).SetRoomId(""), "empty");
        Throws<ArgumentException>(() => p.Slot(1).SetRoomId("   "), "blank");
        Throws<ArgumentException>(() => p.Slot(1).SetRoomId(null), "null");
        Throws<ArgumentException>(() => p.Slot(1).SetRoomId("bad room"), "space");
        Throws<ArgumentException>(() => p.Slot(1).SetRoomId("a/b"), "slash");
        Throws<ArgumentException>(() => p.Slot(1).SetRoomId(new string('x', 65)), "too long");
        p.Slot(1).SetRoomId(new string('x', 64));
        Assert.Equal(64, p.Slot(1).RoomId.Length, "64 ok");
    }

    [Test]
    public static void Room_id_persists_across_processors()
    {
        var dir = Paths.TempData();
        using (var p = new Processor(dir, 2))
        {
            p.Slot(2).SetRoomId("ROOM2");
            Assert.True(File.Exists(Path.Combine(p.Slot(2).Directory, "slot.json")), "slot.json written");
        }
        using var q = new Processor(dir, 2);
        Assert.Equal("ROOM2", q.Slot(2).RoomId, "reloaded");
        Assert.Equal("SLOT01", q.Slot(1).RoomId, "other slot untouched");
    }

    [Test]
    public static void Corrupt_settings_file_falls_back_to_default_and_warns()
    {
        var dir = Paths.TempData();
        var slotDir = Path.Combine(Path.GetFullPath(dir), "program01");
        Directory.CreateDirectory(slotDir);
        File.WriteAllText(Path.Combine(slotDir, "slot.json"), "{ not json");
        using var p = new Processor(dir, 1);
        Assert.Equal("SLOT01", p.Slot(1).RoomId, "default");
        Assert.True(p.RecentLog(1).Any(e => e.Level == "Warning"), "warning logged");
    }

    [Test]
    public static void Duplicate_room_id_is_rejected_case_insensitively()
    {
        using var p = new Processor(Paths.TempData(), 3);
        p.Slot(1).SetRoomId("Lobby");
        Throws<ArgumentException>(() => p.Slot(2).SetRoomId("LOBBY"), "duplicate");
        Throws<ArgumentException>(() => p.Slot(2).SetRoomId("slot03"), "clashes with slot 3's default");
        p.Slot(1).SetRoomId("LOBBY"); // same slot may change case
        Assert.Equal("LOBBY", p.Slot(1).RoomId, "recased");
    }

    [Test]
    public static void Duplicate_room_id_in_hand_edited_files_falls_back_to_default()
    {
        var dir = Paths.TempData();
        foreach (var n in new[] { 1, 2 })
        {
            var sd = Path.Combine(Path.GetFullPath(dir), $"program{n:00}");
            Directory.CreateDirectory(sd);
            File.WriteAllText(Path.Combine(sd, "slot.json"), "{\"roomId\":\"Shared\"}");
        }
        using var p = new Processor(dir, 2);
        Assert.Equal("Shared", p.Slot(1).RoomId, "earlier slot keeps it");
        Assert.Equal("SLOT02", p.Slot(2).RoomId, "later slot falls back");
        Assert.True(p.RecentLog(2).Any(e => e.Level == "Warning"), "warning logged");
    }

    [Test]
    public static void Find_slot_by_room_id_ignores_case()
    {
        using var p = new Processor(Paths.TempData(), 3);
        p.Slot(3).SetRoomId("Boardroom");
        Assert.Equal(3, p.FindSlotByRoomId("boardROOM")!.Number, "custom");
        Assert.Equal(2, p.FindSlotByRoomId("slot02")!.Number, "default");
        Assert.True(p.FindSlotByRoomId("nope") == null, "unknown");
    }

    [Test]
    public static void Setting_room_id_raises_state_changed()
    {
        using var p = new Processor(Paths.TempData(), 1);
        var n = 0;
        p.Slot(1).StateChanged += _ => n++;
        p.Slot(1).SetRoomId("X1");
        Assert.Equal(1, n, "StateChanged");
    }

    [Test]
    public static void Resolve_by_room_id_picks_that_slot()
    {
        using var p = new Processor(Paths.TempData(), 3);
        p.Slot(2).SetRoomId("ROOM2");
        var dev = DeviceDirectory.Resolve(p, 3, null, "room2")!;
        Assert.Equal(2, dev.Slot, "placeholder in slot 2");
        Assert.True(p.Slot(2).FindDevice(3) == dev, "registered in slot 2");
        p.Slot(3).RegisterDevice(5, "XPanel", "x");
        Assert.Equal(3, DeviceDirectory.Resolve(p, 5, null, "SLOT03")!.Slot, "existing device");
        Assert.True(DeviceDirectory.Resolve(p, 3, null, "missing") == null, "unknown room returns null");
        Assert.Equal(2, DeviceDirectory.Resolve(p, 4, 2, "ROOM2")!.Slot, "matching slot + room");
        Throws<ArgumentException>(() => DeviceDirectory.Resolve(p, 4, 1, "ROOM2"), "slot/room disagree");
        Assert.Equal(1, DeviceDirectory.Resolve(p, 9, null)!.Slot, "no room: slot 1 as before");
    }

    [Test]
    public static void Same_ip_id_in_two_slots_warns_once()
    {
        using var p = new Processor(Paths.TempData(), 2);
        p.Slot(1).RegisterDevice(3, "XPanel", "a");
        p.Slot(2).RegisterDevice(3, "XPanel", "b");
        p.Slot(2).RegisterDevice(3, "XPanel", "b");
        Assert.Equal(1, p.RecentLog(2).Count(e => e.Level == "Warning" && e.Message.Contains("slot 1")), "one warning on slot 2");
        Assert.Equal(0, p.RecentLog(1).Count(e => e.Level == "Warning"), "none on slot 1");
    }

    [Test]
    public static void Ipt_report_shows_room_id()
    {
        using var p = new Processor(Paths.TempData(), 1);
        p.Slot(1).SetRoomId("ROOM9");
        Assert.True(p.Slot(1).RunConsoleCommand("ipt").Contains("ROOM9"), "header");
    }
}
