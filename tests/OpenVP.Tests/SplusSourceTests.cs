using System.IO.Compression;
using System.Text;
using OpenVP.Lpz;
using OpenVP.Server;

namespace OpenVP.Tests;

public static class SplusSourceTests
{
    const string Volume = "DIGITAL_INPUT Up;\r\nANALOG_OUTPUT Level;\r\n\r\nPUSH Up\r\n{\r\n    Level = Level + 1;\r\n}\r\n";

    static string Folder() => Directory.CreateTempSubdirectory("openvp-splus").FullName;

    static string LpzWithArchive(string dir, params (string Name, string Text)[] files)
    {
        var path = Path.Combine(dir, "Room.lpz");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Put(ZipArchive z, string name, byte[] c) { using var s = z.CreateEntry(name).Open(); s.Write(c); }
        Put(zip, "boot.bt", Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=room.bin\r\n"));
        using var ms = new MemoryStream();
        using (var inner = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (n, t) in files) Put(inner, n, Encoding.Latin1.GetBytes(t));
        Put(zip, "Room_archive.zip", ms.ToArray());
        return path;
    }

    [Test]
    public static void Sources_next_to_the_program_and_in_its_archive_are_listed_and_read()
    {
        var dir = Folder();
        File.WriteAllText(Path.Combine(dir, "Volume.usp"), Volume);
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "not source");
        var lpz = LpzWithArchive(dir, ("Inner.usp", "// inner\r\nDIGITAL_INPUT a;"), ("Volume.usp", "// older copy"), ("sub.dll", "x"));
        var list = SplusSources.List(lpz);
        Assert.Equal("Inner.usp, Volume.usp", string.Join(", ", list.Select(f => f.Name)), "only source files");
        Assert.Equal("archive", list[0].Origin, "from the archive");
        Assert.Equal("folder", list[1].Origin, "the folder wins over the archive for the same name");
        Assert.Equal(Volume, SplusSources.Read(lpz, "volume.usp", out _), "read, any letter case");
        Assert.Equal("// inner\r\nDIGITAL_INPUT a;", SplusSources.Read(lpz, "Inner.usp", out var f), "read from the archive");
        Assert.Equal("archive", f!.Origin, "origin");
        Assert.True(SplusSources.List(Path.Combine(dir, "missing.lpz")).Count == 1, "a folder is still searched when the program file is gone");
    }

    [Test]
    public static void A_source_name_can_never_leave_the_list()
    {
        var dir = Folder();
        var secret = Path.Combine(Directory.CreateTempSubdirectory("openvp-secret").FullName, "Secret.usp");
        File.WriteAllText(secret, "secret");
        Directory.CreateDirectory(Path.Combine(dir, "sub"));
        File.WriteAllText(Path.Combine(dir, "sub", "Deep.usp"), "deep");
        var lpz = LpzWithArchive(dir, ("Ok.usp", "ok"));
        foreach (var bad in new[] { "../Secret.usp", "..\\Secret.usp", secret, "sub/Deep.usp", "sub\\Deep.usp", "..", "Ok.usp\0.txt", "", "Volume.usp", "Ok.cs", "C:Ok.usp" })
            Assert.True(SplusSources.Read(lpz, bad, out _) == null, "refused: " + bad.Replace("\0", "\\0"));
        Assert.Equal("ok", SplusSources.Read(lpz, "Ok.usp", out _), "the plain name works");
        // Large files and linked files are not offered.
        File.WriteAllText(Path.Combine(dir, "Big.usp"), new string('x', (int)SplusSources.MaxBytes + 1));
        if (!OperatingSystem.IsWindows()) File.CreateSymbolicLink(Path.Combine(dir, "Link.usp"), secret);
        var names = SplusSources.List(lpz).Select(f => f.Name).ToList();
        Assert.True(!names.Contains("Big.usp") && !names.Contains("Link.usp"), "too large and linked files are not listed: " + string.Join(",", names));
    }

    [Test]
    public static void A_compiled_module_is_matched_to_its_source_by_name()
    {
        var sources = new[] { new SplusSourceFile("Volume.usp", "folder", 1), new SplusSourceFile("Other.inc", "folder", 1) };
        var m = SplusSources.Modules(new[] { new SplusModule("S2_Volume.dll", 5), new SplusModule("volume.dll", 5), new SplusModule("Gone.dll", 5) }, sources);
        Assert.Equal("Volume.usp,Volume.usp,", string.Join(",", m.Select(x => x.Source ?? "")), "matched, with or without the S2_ prefix");
    }

    [Test]
    public static void The_endpoints_need_View_and_serve_only_listed_files()
    {
        Assert.True(EndpointPolicy.For("GET", "/api/slots/{n:int}/splus")!.Perm == OpenVP.Core.Security.Perm.View, "list: View, like program download");
        Assert.True(EndpointPolicy.For("GET", "/api/slots/{n:int}/splus/source")!.Perm == EndpointPolicy.For("GET", "/api/slots/{n:int}/files/download")!.Perm, "source: the same as the program files download");

        using var s = new OpsTestServer();
        var dir = Folder();
        var src = Path.Combine(Paths.RepoRoot, "samples", "SimplWindows");
        File.Copy(Path.Combine(src, "SampleProgram.smw"), Path.Combine(dir, "SampleProgram.smw"));
        File.Copy(Path.Combine(src, "SampleProgram.openvp.json"), Path.Combine(dir, "SampleProgram.openvp.json"));
        File.WriteAllText(Path.Combine(dir, "Volume.usp"), Volume);
        var r = s.Post("/api/slots/1/program/path", new { path = Path.Combine(dir, "SampleProgram.smw"), watch = false, start = false });
        Assert.Equal(200, r.Status, "load: " + r.Text);
        var slotDir = s.P.Slot(1).ProgramPath!;
        // The loaded copy lives in the slot's own folder, so the source has to be where the program really is.
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(slotDir)!, "Volume.usp"), Volume);

        var list = s.Get("/api/slots/1/splus");
        Assert.Equal(200, list.Status, "list: " + list.Text);
        Assert.Equal("Volume.usp", list.Json.GetProperty("sources")[0].GetProperty("name").GetString(), "lists it");
        var one = s.Get("/api/slots/1/splus/source?name=Volume.usp");
        Assert.Equal(200, one.Status, "source: " + one.Text);
        Assert.Equal(Volume, one.Json.GetProperty("text").GetString(), "the text");
        Assert.Equal(404, s.Get("/api/slots/1/splus/source?name=..%2FVolume.usp").Status, "no path");
        Assert.Equal(404, s.Get("/api/slots/1/splus/source?name=SampleProgram.smw").Status, "not a source file");
        Assert.Equal(404, s.Get("/api/slots/1/splus/source").Status, "no name");
        Assert.Equal(404, s.Get("/api/slots/2/splus/source?name=Volume.usp").Status, "another slot has no program");
        Assert.Equal(0, s.Get("/api/slots/2/splus").Json.GetProperty("sources").GetArrayLength(), "and no sources");

        // Permission: a Viewer reads it, and a user limited to another slot does not.
        Assert.Equal(200, s.Post("/api/users", new { name = "v", password = "viewer-password-1", role = "viewer" }).Status, "viewer");
        Assert.Equal(200, s.Post("/api/users", new { name = "v2", password = "viewer-password-2", role = "viewer", slots = new[] { 2 } }).Status, "slot-2 viewer");
        using var v = new SecClient(s); Assert.Equal(200, v.Login("v", "viewer-password-1"), "login");
        using var v2 = new SecClient(s); Assert.Equal(200, v2.Login("v2", "viewer-password-2"), "login 2");
        using var anon = new SecClient(s, "wrong");
        Assert.Equal(200, v.Get("/api/slots/1/splus/source?name=Volume.usp").Status, "a Viewer may read program source");
        Assert.Equal(403, v2.Get("/api/slots/1/splus/source?name=Volume.usp").Status, "not another slot's");
        Assert.True(anon.Get("/api/slots/1/splus").Status is 401 or 403, "not without signing in");
    }
}
