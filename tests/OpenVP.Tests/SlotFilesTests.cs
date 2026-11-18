using OpenVP.Core;
using OpenVP.SimplSharpHost;

namespace OpenVP.Tests;

public static class SlotFilesTests
{
    static string Slot() => Directory.CreateTempSubdirectory("openvp-slotfiles").FullName;

    [Test]
    public static void ProcessorPathsMapIntoTheSlotFolders()
    {
        var root = Slot();
        var user = Path.Combine(root, "user", "config", "rooms.json");
        Assert.Equal(user, SlotFiles.Map(root, @"\USER\config\rooms.json"), "backslash, upper case");
        Assert.Equal(user, SlotFiles.Map(root, "/user/config/rooms.json"), "slash, lower case");
        Assert.Equal(user, SlotFiles.Map(root, "User/config/rooms.json"), "no leading separator, mixed case");
        Assert.Equal(Path.Combine(root, "nvram", "a.txt"), SlotFiles.Map(root, @"\NVRAM\a.txt"), "NVRAM");
        Assert.Equal(Path.Combine(root, "nvram"), SlotFiles.Map(root, @"\NVRAM"), "bare NVRAM");
        // A path built from GetApplicationRootDirectory() is already inside the slot.
        Assert.Equal(user, SlotFiles.Map(root, Path.Combine(root, "USER", "config", "rooms.json")), "already mapped");
    }

    [Test]
    public static void PathsCannotLeaveTheSlotFolder()
    {
        var root = Slot();
        foreach (var bad in new[] { @"\USER\..\..\etc\passwd", "/nvram/../../x" })
        {
            var threw = false;
            try { SlotFiles.Map(root, bad); } catch (UnauthorizedAccessException) { threw = true; }
            Assert.True(threw, $"Map rejects {bad}");
        }
        foreach (var bad in new[] { "../nvram/x", "a/../../x", "../../etc" })
        {
            var threw = false;
            try { SlotFiles.Resolve(root, "user", bad); } catch (UnauthorizedAccessException) { threw = true; }
            Assert.True(threw, $"Resolve rejects {bad}");
        }
        var unknown = false;
        try { SlotFiles.Resolve(root, "app", ""); } catch (ArgumentException) { unknown = true; }
        Assert.True(unknown, "Resolve rejects roots other than nvram and user");
    }

    [Test]
    public static void ConsoleAndProgramSeeTheSameFile()
    {
        var root = Slot();
        var fromConsole = SlotFiles.Resolve(root, "USER", "config/rooms.json");
        Directory.CreateDirectory(Path.GetDirectoryName(fromConsole)!);
        File.WriteAllText(fromConsole, "{}");
        Assert.True(File.Exists(SlotFiles.Map(root, @"\User\config\rooms.json")), "program reads what the console wrote");
        Assert.True(Directory.Exists(Path.Combine(root, "user")), "user folder created");
    }

    [Test]
    public static void NamesAreChecked()
    {
        Assert.True(SlotFiles.IsValidName("rooms.json"), "plain name");
        Assert.True(SlotFiles.IsValidName("My Config"), "spaces inside");
        foreach (var bad in new[] { "", " ", "..", ".", "a/b", @"a\b", "a:b", " lead" })
            Assert.True(!SlotFiles.IsValidName(bad), $"rejects '{bad}'");
    }

    [Test]
    public static void LoadingAProgramCreatesNvramAndUser()
    {
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new SimplSharpLoader());
        p.LoadProgram(1, Paths.SampleDll, start: false);
        var dir = p.Slot(1).Directory;
        Assert.True(Directory.Exists(Path.Combine(dir, "nvram")), "nvram folder");
        Assert.True(Directory.Exists(Path.Combine(dir, "user")), "user folder");
        Assert.Equal(Path.Combine(dir, "user", "x.json"), p.Slot(1).MapPath(@"\USER\x.json"), "slot MapPath");
    }
}
