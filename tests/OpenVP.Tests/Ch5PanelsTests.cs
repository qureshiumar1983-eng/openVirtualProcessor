using System.IO.Compression;
using System.Text;
using OpenVP.Core;

namespace OpenVP.Tests;

public static class Ch5PanelsTests
{
    private static MemoryStream Zip(params (string name, string content)[] files)
    {
        var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (name, content) in files)
            {
                var e = z.CreateEntry(name);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(content);
            }
        ms.Position = 0;
        return ms;
    }

    private static (Ch5Panels p, string dir) Make()
    {
        var dir = Directory.CreateTempSubdirectory("openvp-ch5").FullName;
        return (new Ch5Panels(dir), dir);
    }

    private static void Rejects(Ch5Panels p, MemoryStream z, string what)
    {
        try { p.Install(1, 3, z); }
        catch (Ch5Exception) { return; }
        throw new AssertException("should have been refused: " + what);
    }

    [Test]
    public static void InstallsAndServesAProject()
    {
        var (p, dir) = Make();
        var info = p.Install(2, 0x0A, Zip(("index.html", "<html></html>"), ("assets/app.js", "1"), ("assets/sub/x.css", "2")), "room.ch5z");
        Assert.True(p.Exists(2, 0x0A), "installed");
        Assert.Equal(3, info.Files, "file count");
        Assert.True(Directory.Exists(Path.Combine(dir, "panels", "slot02", "ipid0A")), "data/panels/slotNN/ipidXX");
        Assert.True(p.ResolveFile(2, 0x0A, "assets/sub/x.css") != null, "nested file");
        Assert.True(p.ResolveFile(2, 0x0A, "") != null, "empty path is index.html");
        Assert.True(p.ResolveFile(2, 0x0A, "nope.js") == null, "missing file");
        Assert.True(p.ResolveFile(2, 0x0B, "") == null, "other IP ID has no project");
        Assert.Equal(1, p.List().Count, "listed");
        Assert.Equal("room.ch5z", p.Get(2, 0x0A)!.Name, "name");
        Assert.True(p.Remove(2, 0x0A) && !p.Exists(2, 0x0A), "removed");
    }

    [Test]
    public static void ReplacingAProjectLeavesNoOldFiles()
    {
        var (p, _) = Make();
        p.Install(1, 3, Zip(("index.html", "v1"), ("old.js", "x")));
        p.Install(1, 3, Zip(("index.html", "v2")));
        Assert.True(p.ResolveFile(1, 3, "old.js") == null, "old file gone");
        Assert.Equal("v2", File.ReadAllText(p.ResolveFile(1, 3, "")!), "new index");
    }

    [Test]
    public static void ProjectInsideOneFolderIsServedFromThatFolder()
    {
        var (p, _) = Make();
        p.Install(1, 3, Zip(("MyProject/index.html", "hi"), ("MyProject/a/b.js", "1")));
        Assert.True(p.ResolveFile(1, 3, "a/b.js") != null, "re-rooted");
    }

    [Test]
    public static void ZipSlipIsRefused()
    {
        var (p, dir) = Make();
        Rejects(p, Zip(("index.html", "x"), ("../evil.txt", "pwned")), "../");
        Rejects(p, Zip(("index.html", "x"), ("a/../../evil.txt", "pwned")), "a/../../");
        Rejects(p, Zip(("index.html", "x"), ("..\\evil.txt", "pwned")), "backslash");
        Rejects(p, Zip(("index.html", "x"), ("/etc/evil.txt", "pwned")), "absolute");
        Rejects(p, Zip(("index.html", "x"), ("C:/evil.txt", "pwned")), "drive");
        Rejects(p, Zip(("index.html", "x"), ("C:\\Windows\\evil.txt", "pwned")), "drive backslash");
        Assert.True(!p.Exists(1, 3), "nothing installed after a refusal");
        Assert.True(!File.Exists(Path.Combine(dir, "evil.txt")) && !File.Exists(Path.Combine(dir, "panels", "evil.txt")) && !File.Exists(Path.Combine(dir, "panels", "slot01", "evil.txt")), "nothing written outside");
        Assert.True(!Directory.GetFileSystemEntries(Path.Combine(dir, "panels", "slot01")).Any(), "scratch folder cleaned up");
    }

    [Test]
    public static void RefusesNonZipNoIndexAndTooManyFiles()
    {
        var (p, _) = Make();
        Rejects(p, new MemoryStream(Encoding.UTF8.GetBytes("this is not a zip")), "not a zip");
        Rejects(p, Zip(("readme.txt", "no start page")), "no index.html");
        var many = new MemoryStream();
        using (var z = new ZipArchive(many, ZipArchiveMode.Create, true))
            for (var i = 0; i <= Ch5Panels.MaxFiles; i++) z.CreateEntry($"f{i}.txt");
        many.Position = 0;
        Rejects(p, many, "too many files");
    }

    [Test]
    public static void ResolveFileCannotLeaveTheProject()
    {
        var (p, dir) = Make();
        p.Install(1, 3, Zip(("index.html", "x")));
        File.WriteAllText(Path.Combine(dir, "secret.txt"), "s");
        Assert.True(p.ResolveFile(1, 3, "../../../secret.txt") == null, "../ blocked");
        Assert.True(p.ResolveFile(1, 3, "..\\..\\secret.txt") == null, "..\\ blocked");
        Assert.True(p.ResolveFile(1, 3, "a/../../x") == null, "inner .. blocked");
        Assert.True(p.ResolveFile(1, 3, "/etc/passwd") == null, "absolute blocked");
        Assert.True(p.ResolveFile(1, 3, "C:/Windows/win.ini") == null, "drive blocked");
        Assert.True(p.ResolveFile(1, 3, "index.html\0.js") == null, "nul blocked");
    }

    [Test]
    public static void SafeEntryPathRules()
    {
        Assert.Equal("a/b.js", Ch5Panels.SafeEntryPath("a/b.js"), "plain");
        Assert.Equal("a/b.js", Ch5Panels.SafeEntryPath("a\\b.js"), "backslashes");
        Assert.Equal("a/b.js", Ch5Panels.SafeEntryPath("./a/./b.js"), "dots");
        Assert.True(Ch5Panels.SafeEntryPath("folder/") == null, "folder entry");
    }
}
