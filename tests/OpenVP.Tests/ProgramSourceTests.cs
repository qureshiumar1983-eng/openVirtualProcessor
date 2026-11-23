using OpenVP.Core;

namespace OpenVP.Tests;

public static class ProgramSourceTests
{
    static string Folder(params string[] files)
    {
        var dir = Directory.CreateTempSubdirectory("openvp-source").FullName;
        foreach (var f in files) File.WriteAllText(Path.Combine(dir, f), f);
        return dir;
    }

    static string Names(IReadOnlyList<ProgramFile> files) =>
        string.Join(", ", files.Select(f => Path.GetFileName(f.Source) == f.Name ? f.Name : $"{Path.GetFileName(f.Source)}>{f.Name}"));

    [Test]
    public static void AnLpzBringsItsSigAndSidecars()
    {
        var dir = Folder("Room.lpz", "Room.sig", "Room.primitives.json", "Other.smw");
        Assert.Equal("Room.lpz, Room.sig, Room.primitives.json", Names(ProgramSource.Companions(Path.Combine(dir, "Room.lpz"))), "companions");
    }

    [Test]
    public static void TheOnlySigInTheFolderIsRenamedToMatch()
    {
        var dir = Folder("Room 2026-11-01.lpz", "Room.sig");
        Assert.Equal("Room 2026-11-01.lpz, Room.sig>Room 2026-11-01.sig", Names(ProgramSource.Companions(Path.Combine(dir, "Room 2026-11-01.lpz"))), "renamed .sig");

        var two = Folder("Room.lpz", "A.sig", "B.sig");
        Assert.Equal("Room.lpz", Names(ProgramSource.Companions(Path.Combine(two, "Room.lpz"))), "two .sigs with other names: can't tell which");
    }

    [Test]
    public static void AChosenSigWinsOverTheOneNextToTheProgram()
    {
        var dir = Folder("Room.lpz", "Room.sig");
        var other = Folder("Copy.sig");
        Assert.Equal("Room.lpz, Copy.sig>Room.sig", Names(ProgramSource.Companions(Path.Combine(dir, "Room.lpz"), new[] { Path.Combine(other, "Copy.sig") })), "chosen .sig");
    }

    [Test]
    public static void ASmwBringsItsLpzAndThatOnesSig()
    {
        var dir = Folder("Room.smw", "Room.openvp.json", "Room_compiled.lpz", "Room_compiled.sig");
        Assert.Equal("Room.smw, Room_compiled.lpz, Room_compiled.sig, Room.openvp.json",
            Names(ProgramSource.Companions(Path.Combine(dir, "Room.smw"))), "companions");
    }

    [Test]
    public static void WatchRaisesOnceAfterTheFilesSettle()
    {
        var dir = Folder("Room.lpz", "Room.sig");
        var lpz = Path.Combine(dir, "Room.lpz");
        var count = 0;
        using var w = new SourceWatch(lpz, () => ProgramSource.Companions(lpz).Select(f => f.Source), TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(100));
        w.Changed += () => Interlocked.Increment(ref count);
        Thread.Sleep(200);
        Assert.Equal(0, Volatile.Read(ref count), "nothing changed");

        File.WriteAllText(lpz, "compiled again");
        File.WriteAllText(Path.Combine(dir, "Room.sig"), "new names, and longer");
        Assert.Eventually(() => Volatile.Read(ref count) == 1, "reload after the compile");
        Thread.Sleep(200);
        Assert.Equal(1, Volatile.Read(ref count), "only once per change");

        File.Delete(lpz);
        Thread.Sleep(200);
        Assert.Equal(1, Volatile.Read(ref count), "not while the program file is missing");
        File.WriteAllText(lpz, "back");
        Assert.Eventually(() => Volatile.Read(ref count) == 2, "reload when it comes back");
    }
}
