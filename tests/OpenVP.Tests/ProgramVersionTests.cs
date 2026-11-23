using System.Text;
using OpenVP.Core;
using OpenVP.Core.Ops;
using OpenVP.Server;

namespace OpenVP.Tests;

/// <summary>Program version history (dedupe by hash, the cap) and the program diff on two variants of the sample .smw.</summary>
public static class ProgramVersionTests
{
    static (string, byte[])[] Files(string main, string content, string? sig = null)
    {
        var l = new List<(string, byte[])> { (main, Encoding.UTF8.GetBytes(content)) };
        if (sig != null) l.Add((Path.ChangeExtension(main, ".sig"), Encoding.UTF8.GetBytes(sig)));
        return l.ToArray();
    }

    [Test]
    public static void Same_files_are_the_same_version_and_the_hash_ignores_order()
    {
        var data = Paths.TempData();
        var v = new ProgramVersions(data);
        var a = v.Record(1, Files("Room.smw", "one", "sig1"), "upload", "this computer", "SIMPL Windows");
        Assert.True(a.Id.Length == 12 && a.Loads == 1 && a.How == "upload" && a.File == "Room.smw" && a.Name == "Room" && a.Files.Length == 2, "first load: " + a);
        var b = v.Record(1, Files("Room.smw", "one", "sig1").Reverse().ToArray(), "auto-reload", "auto");
        Assert.Equal(a.Id, b.Id, "same files, same id, whatever the order");
        Assert.Equal(1, v.List(1).Count, "one version, not two");
        Assert.True(b.Loads == 2 && b.How == "upload" && b.LastHow == "auto-reload" && b.LastWho == "auto" && b.LoadedAt >= a.LoadedAt, "load count and last load updated, first kept");
        var c = v.Record(1, Files("Room.smw", "one", "sig2"), "path", "10.0.0.5");
        Assert.True(c.Id != a.Id, "a changed sidecar is another version");
        Assert.Equal(2, v.List(1).Count, "two versions");
        Assert.Equal(0, v.List(2).Count, "slots are separate");
        var files = v.ReadFiles(1, a.Id)!;
        Assert.True(files[0].Name == "Room.smw" && Encoding.UTF8.GetString(files[0].Data) == "one" && files.Count == 2, "the program is read back first, with its sidecar");
        Assert.True(v.ZipOf(1, a.Id)!.Length > 50, "zip for download");
    }

    [Test]
    public static void Only_the_newest_versions_are_kept()
    {
        var data = Paths.TempData();
        var t = DateTimeOffset.Parse("2025-01-01T00:00:00Z");
        var v = new ProgramVersions(data, keep: 3, clock: () => t = t.AddMinutes(1));
        var ids = new List<string>();
        for (var i = 0; i < 6; i++) ids.Add(v.Record(1, Files("P.smw", "content " + i), "upload", "me").Id);
        var left = v.List(1);
        Assert.Equal(3, left.Count, "capped at three");
        Assert.Equal(string.Join("|", ids.Skip(3).Reverse()), string.Join("|", left.Select(x => x.Id)), "the three newest, newest first");
        Assert.True(!Directory.Exists(Path.Combine(data, "versions", "slot01", ids[0])), "the oldest folders are deleted from disk");
        Assert.True(Directory.Exists(Path.Combine(data, "versions", "slot01", ids[5])), "the newest is kept");
        // Loading an old kept version again makes it the newest, so it survives the next cap.
        v.Record(1, Files("P.smw", "content 3"), "version", "me");
        v.Record(1, Files("P.smw", "content 6"), "upload", "me");
        v.Record(1, Files("P.smw", "content 7"), "upload", "me");
        Assert.Equal(3, v.List(1).Count, "cap still holds");
        v.SetKeep(1);
        Assert.Equal(1, v.List(1).Count, "lowering the cap prunes");
        Assert.Equal(1, new ProgramVersions(data).Keep, "and is remembered");
        Assert.True(Assert.Throws<ArgumentException>(() => v.SetKeep(0)).Message.Contains("1 and 1000"), "range");
    }

    [Test]
    public static void Notes_delete_and_ids()
    {
        var v = new ProgramVersions(Paths.TempData());
        var a = v.Record(1, Files("P.smw", "x"), "upload", "me");
        Assert.Equal("Before the client demo", v.SetNote(1, a.Id, "  Before the client demo ")!.Note, "note trimmed");
        Assert.True(v.SetNote(1, a.Id, "")!.Note == null, "empty clears");
        Assert.True(v.SetNote(1, "ffffffffffff", "x") == null, "unknown");
        Assert.True(Assert.Throws<ArgumentException>(() => v.SetNote(1, a.Id, new string('x', 600))).Message.Contains("500"), "length");
        Assert.True(v.Get(1, "../../x") == null && v.FolderOf(1, "..") == null, "an id can't be a path");
        Assert.True(v.Delete(1, a.Id) && v.List(1).Count == 0 && !v.Delete(1, a.Id), "delete");
    }

    [Test]
    public static void Versions_from_an_import_are_merged_not_replaced()
    {
        var a = new ProgramVersions(Paths.TempData());
        var x = a.Record(1, Files("A.smw", "from a"), "upload", "me");
        var b = new ProgramVersions(Paths.TempData());
        b.Record(1, Files("B.smw", "own"), "upload", "me");
        Assert.Equal(1, b.Merge(1, Path.Combine(a.FolderOf(1, x.Id)!, "..")), "one added");
        Assert.Equal(2, b.List(1).Count, "both there");
        Assert.True(b.ReadFiles(1, x.Id) != null, "and readable");
        Assert.Equal(0, b.Merge(1, Path.Combine(a.FolderOf(1, x.Id)!, "..")), "merging again adds nothing");
    }

    // ------------------------------------------------------------------ diff

    static string Sample(string name) => Path.Combine(Paths.RepoRoot, "samples", "SimplWindows", name);

    static string MakeVariant(string root, string label, Func<string, string> smw, Func<string, string> sidecar)
    {
        var dir = Path.Combine(root, label);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SampleProgram.smw"), smw(File.ReadAllText(Sample("SampleProgram.smw"))), Encoding.Latin1);
        File.WriteAllText(Path.Combine(dir, "SampleProgram.openvp.json"), sidecar(File.ReadAllText(Sample("SampleProgram.openvp.json"))));
        return dir;
    }

    [Test]
    public static void Diff_of_two_smw_variants_reports_devices_signals_symbols_and_files()
    {
        var root = Paths.TempData();
        var a = MakeVariant(root, "a", s => s, s => s);
        var b = MakeVariant(root, "b",
            smw => smw
                .Replace("P1=\"Hello from SIMPL\"", "P1=\"Hello again\"")                                  // a parameter changed
                .Replace("Nm=both_pressed\n", "Nm=both_pressed_v2\n")                                      // a signal renamed
                .Replace("[\nObjTp=Sg\nH=1\n", "[\nObjTp=Sm\nH=45\nSmC=0\nNm=OR\nmI=2\nI1=25\nI2=26\nmO=1\nO1=50\n]\n[\nObjTp=Sg\nH=50\nNm=either_pressed\n]\n[\nObjTp=Sg\nH=1\n"), // a symbol and a signal added
            side => side.Replace("\"21\": \"03\"", "\"21\": \"04\""));                                       // the panel moved to IP ID 04
        var profiler = new ProgramProfiler(root);
        var pa = profiler.Build("version A", a, "SampleProgram.smw");
        var pb = profiler.Build("version B", b, "SampleProgram.smw");
        Assert.Equal("SIMPL Windows", pa.Kind, "kind");
        Assert.True(pa.HasSymbols && pa.Symbols.Count >= 6, "symbols read from the .smw: " + pa.Symbols.Count);
        Assert.True(pa.Signals.Count > 10 && pa.Devices.Count >= 2, $"signals and devices: {pa.Signals.Count} / {pa.Devices.Count}");

        var r = ProgramDiff.Compare(pa, pb);
        Assert.True(!r.Identical, "different");
        var text = r.Text();
        Section(r, "Devices and IP IDs", i => i.Name == "IP ID 03" && i.Change == "removed", text);
        Section(r, "Devices and IP IDs", i => i.Name == "IP ID 04" && i.Change == "added", text);
        Section(r, "Signals", i => i.Name == "both_pressed" && i.Change == "removed", text);
        Section(r, "Signals", i => i.Name == "both_pressed_v2" && i.Change == "added", text);
        Section(r, "Signals", i => i.Name == "either_pressed" && i.Change == "added", text);
        Section(r, "Symbols", i => i.Name.StartsWith("OR") && i.Change == "added", text);
        Section(r, "Symbols", i => i.Name.StartsWith("Serial Send") && i.Change == "changed" && i.Detail!.Contains("Hello again"), text);
        Section(r, "Symbols", i => i.Name.StartsWith("AND") && i.Change == "changed" && i.Detail!.Contains("both_pressed_v2"), text);
        Section(r, "Files", i => i.Name == "SampleProgram.smw" && i.Change == "changed", text);
        Section(r, "Files", i => i.Name == "SampleProgram.openvp.json" && i.Change == "changed", text);
        Assert.True(text.StartsWith("Comparing version A with version B") && text.Contains("+ OR") && text.Contains("- both_pressed"), "readable text report:\n" + text);
        Assert.True(r.Summary.Any(x => x.StartsWith("Signals:")), "summary");

        var same = ProgramDiff.Compare(pa, profiler.Build("again", a, "SampleProgram.smw"));
        Assert.True(same.Identical && same.Summary.Count == 0 && same.Text().Contains("No differences"), "a program compared with itself has no differences");
    }

    static void Section(DiffReport r, string title, Func<DiffItem, bool> has, string text) =>
        Assert.True(r.Sections.First(s => s.Title == title).Items.Any(has), $"{title} has the expected item. Report:\n{text}");

    [Test]
    public static void Diff_handles_a_file_that_isnt_a_program()
    {
        var root = Paths.TempData();
        var d = Path.Combine(root, "bad");
        Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "Broken.dll"), "not a dll");
        var p = new ProgramProfiler(root).Build("broken", d, "Broken.dll");
        Assert.True(p.Notes.Any(n => n.Contains("couldn't be read")) && p.Files.Count == 1, "noted, not thrown");
        var ok = new ProgramProfiler(root).Build("sample", Path.GetDirectoryName(Sample("SampleProgram.smw"))!, "SampleProgram.smw");
        var r = ProgramDiff.Compare(ok, p);
        Assert.True(!r.Identical && r.Notes.Count > 0, "still compares what it can");
    }
}
