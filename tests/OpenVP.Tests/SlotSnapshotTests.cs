using OpenVP.Core;

namespace OpenVP.Tests;

public static class SlotSnapshotTests
{
    static string Data() => Directory.CreateTempSubdirectory("openvp-snap").FullName;

    static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    // A slot 2 with a program file, NVRAM and User content, and a slot.json.
    static string Setup(string data)
    {
        Write(Path.Combine(data, "uploads", "slot02", "Prog.smw"), "program-v1");
        Write(Path.Combine(data, "program02", "nvram", "x.txt"), "nvram-v1");
        Write(Path.Combine(data, "program02", "user", "config", "rooms.json"), "user-v1");
        Write(Path.Combine(data, "program02", "slot.json"), "{\"roomId\":\"A\"}");
        return data;
    }

    [Test]
    public static void Take_and_restore_round_trip()
    {
        var data = Setup(Data());
        var s = new SlotSnapshots(data);
        var info = s.Take(2, "Prog", "Prog.smw", SlotSnapshots.BeforeLoad);
        Assert.True(info != null, "snapshot taken");
        Assert.True(info!.Size >= "program-v1nvram-v1user-v1".Length, "size counts all three folders");
        Assert.Equal(1, s.List(2).Count, "listed");

        Write(Path.Combine(data, "uploads", "slot02", "Prog.smw"), "program-v2");
        Write(Path.Combine(data, "uploads", "slot02", "Extra.sig"), "new");
        Write(Path.Combine(data, "program02", "nvram", "x.txt"), "nvram-v2");
        File.Delete(Path.Combine(data, "program02", "user", "config", "rooms.json"));

        var restored = s.Restore(2, info.Id);
        Assert.True(restored != null, "restored");
        Assert.Equal("program-v1", File.ReadAllText(Path.Combine(data, "uploads", "slot02", "Prog.smw")), "program file");
        Assert.True(!File.Exists(Path.Combine(data, "uploads", "slot02", "Extra.sig")), "file added later is gone");
        Assert.Equal("nvram-v1", File.ReadAllText(Path.Combine(data, "program02", "nvram", "x.txt")), "nvram");
        Assert.Equal("user-v1", File.ReadAllText(Path.Combine(data, "program02", "user", "config", "rooms.json")), "user subfolder");
        Assert.Equal(Path.Combine(data, "uploads", "slot02", "Prog.smw"), s.RestoredProgramPath(2, restored!), "program path");
    }

    [Test]
    public static void Slot_json_is_not_snapshotted_or_restored()
    {
        var data = Setup(Data());
        var s = new SlotSnapshots(data);
        var info = s.Take(2, "Prog", "Prog.smw", SlotSnapshots.Manual)!;
        Assert.True(!Directory.EnumerateFiles(Path.Combine(data, "snapshots", "slot02", info.Id), "slot.json", SearchOption.AllDirectories).Any(), "not copied");
        Write(Path.Combine(data, "program02", "slot.json"), "{\"roomId\":\"B\"}");
        s.Restore(2, info.Id);
        Assert.Equal("{\"roomId\":\"B\"}", File.ReadAllText(Path.Combine(data, "program02", "slot.json")), "slot.json untouched");
    }

    [Test]
    public static void Only_the_newest_ten_are_kept()
    {
        var data = Setup(Data());
        var s = new SlotSnapshots(data);
        var ids = new List<string>();
        for (var i = 0; i < 13; i++) ids.Add(s.Take(2, "Prog", "Prog.smw", SlotSnapshots.BeforeLoad)!.Id);
        var list = s.List(2);
        Assert.Equal(10, list.Count, "kept 10");
        Assert.Equal(ids[12], list[0].Id, "newest first");
        Assert.True(s.Get(2, ids[0]) == null && s.Get(2, ids[2]) == null, "oldest three deleted");
        Assert.True(s.Get(2, ids[3]) != null, "fourth oldest kept");
    }

    [Test]
    public static void Invalid_ids_are_rejected()
    {
        var data = Setup(Data());
        var s = new SlotSnapshots(data);
        s.Take(2, "Prog", "Prog.smw", SlotSnapshots.Manual);
        foreach (var bad in new[] { null, "", "..", "../uploads", "..\\x", "a/b", "20240101T000000Z-001/..", "/etc", "20240101T000000Z-1" })
        {
            Assert.True(!SlotSnapshots.IsValidId(bad), $"invalid: {bad}");
            Assert.True(s.Get(2, bad) == null, $"Get {bad}");
            Assert.True(s.Restore(2, bad) == null, $"Restore {bad}");
            Assert.True(!s.Delete(2, bad), $"Delete {bad}");
        }
        Assert.True(Directory.Exists(Path.Combine(data, "uploads")), "nothing outside was deleted");
        Assert.True(SlotSnapshots.IsValidId(s.List(2)[0].Id), "generated ids are valid");
    }

    [Test]
    public static void Empty_slot_takes_no_snapshot()
    {
        var data = Data();
        var s = new SlotSnapshots(data);
        Assert.True(s.Take(3, null, null, SlotSnapshots.Manual) == null, "nothing to save");
        Assert.Equal(0, s.List(3).Count, "none listed");
        Directory.CreateDirectory(Path.Combine(data, "program03", "nvram"));
        Assert.True(s.Take(3, null, null, SlotSnapshots.Manual) == null, "empty folders only");
        Write(Path.Combine(data, "program03", "nvram", "a.txt"), "a");
        Assert.True(s.Take(3, null, null, SlotSnapshots.Manual) != null, "files alone are worth saving");
    }

    [Test]
    public static void Delete_removes_one_snapshot()
    {
        var data = Setup(Data());
        var s = new SlotSnapshots(data);
        var a = s.Take(2, "Prog", "Prog.smw", SlotSnapshots.Manual)!;
        var b = s.Take(2, "Prog", "Prog.smw", SlotSnapshots.Manual)!;
        Assert.True(s.Delete(2, a.Id), "deleted");
        Assert.True(!s.Delete(2, a.Id), "gone");
        Assert.Equal(b.Id, s.List(2).Single().Id, "other stays");
    }
}
