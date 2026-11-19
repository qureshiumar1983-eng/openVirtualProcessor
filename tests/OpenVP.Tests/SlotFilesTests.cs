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

public static class ProgramFolderTests
{
    [Test]
    public static void MakeProgramFilenameInsertsTheProgramFolder()
    {
        Assert.Equal(@"\NVRAM\Lobby AV\config.txt", SlotFiles.ProgramFilename(@"\NVRAM\config.txt", "Lobby AV"), "NVRAM file");
        Assert.Equal(@"\nvram\Lobby AV\sub\a.txt", SlotFiles.ProgramFilename("/nvram/sub/a.txt", "Lobby AV"), "keeps case, slashes become backslashes");
        Assert.Equal(@"\USER\Lobby AV\x.json", SlotFiles.ProgramFilename(@"\USER\x.json", "Lobby AV"), "USER file");
        Assert.Equal(@"\NVRAM\Lobby AV\", SlotFiles.ProgramFilename(@"\NVRAM", "Lobby AV"), "bare root");
        Assert.Equal(@"\USER\Lobby AV\x.json", SlotFiles.ProgramFilename("x.json", "Lobby AV"), "no top folder");
        Assert.Equal(@"\NVRAM\A_B\c.txt", SlotFiles.ProgramFilename(@"\NVRAM\c.txt", "A/B"), "unsafe characters replaced");
        Assert.Equal(@"\NVRAM\Program\c.txt", SlotFiles.ProgramFilename(@"\NVRAM\c.txt", null), "no program loaded");
    }

    static string Lpz(string header, string? archivedSmw = null)
    {
        var path = Path.Combine(Paths.TempData(), "p.lpz");
        var bin = LpzTests.Record(31, "LeadingREM", [], [], LpzTests.Int(15, 1), LpzTests.Str(3, header));
        using (var zip = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create))
        {
            void Put(string name, byte[] data) { using var s = zip.CreateEntry(name).Open(); s.Write(data); }
            Put("boot.bt", System.Text.Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=p.bin\r\nSMWFileName=file_name.smw\r\n"));
            Put("p.bin", bin);
            if (archivedSmw != null)
            {
                var ms = new MemoryStream();
                using (var inner = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
                using (var s = inner.CreateEntry("file_name.smw").Open()) s.Write(System.Text.Encoding.ASCII.GetBytes(archivedSmw));
                Put("file_name_archive.zip", ms.ToArray());
            }
        }
        return path;
    }

    [Test]
    public static void LpzProgramNameComesFromTheHeader()
    {
        Assert.Equal("Lobby AV", OpenVP.Lpz.LpzPackage.Open(Lpz("Program File: file_name.smw\r\nProgram Name: Lobby AV\r\n")).ProgramName, "compile header");
        Assert.Equal("Board Room", OpenVP.Lpz.LpzPackage.Open(Lpz("Program File: file_name.smw", "[\r\nObjTp=Hd\r\nPgmNm=Board Room\r\n]\r\n")).ProgramName, "archived .smw");
        Assert.Equal("file_name", OpenVP.Lpz.LpzPackage.Open(Lpz("Program File: file_name.smw")).ProgramName, "falls back to the file name");
    }
}

public static class MissingFileTests
{
    static (MissingFileLog log, List<string> lines, string dir) New()
    {
        var dir = Directory.CreateTempSubdirectory("openvp-missing").FullName;
        SlotFiles.EnsureRoots(dir);
        var lines = new List<string>();
        return (new MissingFileLog(() => dir, (_, m) => lines.Add(m)), lines, dir);
    }

    [Test]
    public static void AMissingConfigFileSuggestsTheFolderToCreate()
    {
        var (log, lines, dir) = New();
        var m = log.Report(@"\USER\config\rooms\rooms.json");
        Assert.True(m != null, "reported");
        Assert.Equal("user", m!.Root, "root");
        Assert.Equal("config/rooms", m.Folder, "folder inside the root");
        Assert.Equal("rooms.json", m.Name, "file name");
        Assert.Equal(@"\USER\config\rooms\rooms.json", m.ProgramPath, "program path");
        Assert.Equal(@"\USER\config\rooms\", m.FolderPath, "folder path");
        Assert.Equal(Path.Combine(dir, "user", "config", "rooms"), m.DiskFolder, "folder on disk");
        Assert.True(!m.FolderExists, "folder isn't there yet");
        Assert.Equal(1, lines.Count, "logged once");
        Assert.True(lines[0].Contains(@"Create the folder \USER\config\rooms\") && lines[0].Contains(m.DiskFolder), "log says which folder to create and where: " + lines[0]);

        log.Report("/user/config/rooms/rooms.json");
        Assert.Equal(1, lines.Count, "same file, other spelling: not logged again");
        Assert.Equal(1, log.Items.Count, "listed once");
    }

    [Test]
    public static void OnlyTheFileMissingSaysWhereToPutIt()
    {
        var (log, lines, dir) = New();
        Directory.CreateDirectory(Path.Combine(dir, "nvram", "cfg"));
        var m = log.Report(@"\NVRAM\cfg\a.txt")!;
        Assert.True(m.FolderExists, "folder is there");
        Assert.True(lines[0].Contains(@"Put a.txt in \NVRAM\cfg\") && lines[0].Contains("NVRAM tab"), "log: " + lines[0]);
    }

    [Test]
    public static void MissingFoldersAndExistingFilesAndOtherPaths()
    {
        var (log, lines, dir) = New();
        var f = log.Report(@"\USER\logs", isFolder: true)!;
        Assert.True(f.IsFolder && f.Folder == "logs" && f.FolderPath == @"\USER\logs\", "a missing folder is the folder itself");

        File.WriteAllText(Path.Combine(dir, "user", "here.json"), "{}");
        Assert.True(log.Report(@"\USER\here.json") == null, "a file that's there isn't reported");
        Assert.True(log.Report(@"\HTML\index.html") == null, "only NVRAM and USER, the folders the console can fill");
        Assert.True(log.Report(@"\USER\..\..\x") == null, "paths outside the slot are ignored, not thrown");
        Assert.True(log.Report("") == null, "empty path");
        Assert.Equal(1, lines.Count, "only the folder was logged");
    }

    [Test]
    public static void PuttingTheFileInPlaceTakesItOffTheList()
    {
        var (log, _, dir) = New();
        log.Report(@"\USER\config\rooms.json");
        Assert.Equal(1, log.Items.Count, "listed");
        Directory.CreateDirectory(Path.Combine(dir, "user", "config"));
        Assert.True(log.Items[0].FolderExists, "folder made");
        File.WriteAllText(Path.Combine(dir, "user", "config", "rooms.json"), "{}");
        Assert.Equal(0, log.Items.Count, "gone once the file is there");
    }

    [Test]
    public static void SlotsReportMissingFilesToTheirLog()
    {
        using var p = new Processor(Paths.TempData());
        var slot = p.Slot(2);
        slot.ReportMissing(@"\USER\config\rooms.json");
        Assert.Equal(1, slot.MissingFiles.Items.Count, "listed on the slot");
        Assert.Equal(0, p.Slot(1).MissingFiles.Items.Count, "other slots unaffected");
        Assert.Equal(Path.Combine(slot.Directory, "user", "config"), slot.MissingFiles.Items[0].DiskFolder, "inside the slot folder");
    }
}

public static class MissingFileApiTests
{
    [Test]
    public static void SimplSharpFileApiReportsMissingConfigFiles()
    {
        using var p = new Processor(Paths.TempData());
        var slot = p.Slot(3);
        var before = OpenVP.SimplSharp.OpenVPBridge.Slot;
        OpenVP.SimplSharp.OpenVPBridge.Slot = slot;
        try
        {
            Assert.True(!Crestron.SimplSharp.CrestronIO.File.Exists(@"\USER\config\rooms.json"), "File.Exists is false");
            var threw = false;
            try { Crestron.SimplSharp.CrestronIO.File.OpenRead(@"\NVRAM\settings\a.json"); }
            catch (FileNotFoundException) { threw = true; }
            Assert.True(threw, "opening a missing file for reading still throws FileNotFoundException");
            Assert.True(!Directory.Exists(Path.Combine(slot.Directory, "nvram", "settings")), "reading doesn't make an empty folder");
            Assert.True(!Crestron.SimplSharp.CrestronIO.Directory.Exists(@"\USER\logs"), "Directory.Exists is false");
            var paths = slot.MissingFiles.Items.Select(m => m.ProgramPath).ToList();
            Assert.Equal(@"\USER\config\rooms.json|\NVRAM\settings\a.json|\USER\logs\", string.Join("|", paths), "all three listed");

            using (var w = new Crestron.SimplSharp.CrestronIO.StreamWriter(@"\USER\config\rooms.json")) w.Write("{}");
            Assert.True(Crestron.SimplSharp.CrestronIO.File.Exists(@"\USER\config\rooms.json"), "writing still creates the folder");
            Assert.Equal(2, slot.MissingFiles.Items.Count, "written file drops off the list");
        }
        finally { OpenVP.SimplSharp.OpenVPBridge.Slot = before; }
    }
}
