using System.IO.Compression;
using System.Text;
using OpenVP.Lpz;
using static OpenVP.Tests.LpzTests;

namespace OpenVP.Tests;

public static class LpzSurveyTests
{
    static byte[] Sig(params (string name, int index, byte kind)[] entries)
    {
        var data = new List<byte>(Encoding.ASCII.GetBytes("[LOGOSSIG001.000]"));
        foreach (var (name, index, kind) in entries)
        {
            var text = Encoding.Unicode.GetBytes(name);
            data.AddRange(BitConverter.GetBytes((ushort)(text.Length + 8)));
            data.AddRange(text);
            data.AddRange(BitConverter.GetBytes(index));
            data.Add(kind); data.Add(1);
        }
        return data.ToArray();
    }

    /// <summary>A program with a known type (AND), an unknown type 999 and a source archive holding the sample .smw.</summary>
    static string BuildProgram(bool withArchive)
    {
        var bin = new List<byte>();
        bin.AddRange(Record(31, "LeadingREM", [], [], Int(15, 1), Str(3, "Program File: survey.smw")));
        bin.AddRange(Record(0, "S-1", [10, 11], [12]));                                   // AND: runs
        bin.AddRange(Record(999, "S-2", [10, 1], [13], Int(15, 2), Int(2, 250), Str(3, "abc"), Int(44, 7)));
        bin.AddRange(Record(999, "S-3", [11], [14, 15]));                                 // second shape

        var dir = Paths.TempData();
        var path = Path.Combine(dir, "survey.lpz");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Put(ZipArchive z, string name, byte[] content) { using var s = z.CreateEntry(name).Open(); s.Write(content); }
        Put(zip, "boot.bt", Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=survey.bin\r\nSMWFileName=survey.smw\r\nRackType=CP4\r\n"));
        Put(zip, "survey.bin", bin.ToArray());
        var sig = Sig(("0", 0, 128), ("1", 1, 128), ("press_a", 10, 0), ("press_b", 11, 0), ("both", 12, 0),
                      ("toggle_fb", 13, 0), ("::inner:S-3:S-1.0000000E", 14, 0));
        if (withArchive)
        {
            using var ms = new MemoryStream();
            using (var inner = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                Put(inner, "survey.smw", File.ReadAllBytes(Path.Combine(Paths.RepoRoot, "samples", "SimplWindows", "SampleProgram.smw")));
                Put(inner, "survey.sig", sig);
            }
            Put(zip, "survey_archive.zip", ms.ToArray());
        }
        else File.WriteAllBytes(Path.Combine(dir, "survey.sig"), sig);
        return path;
    }

    [Test]
    public static void Survey_lists_unrunnable_types_with_names_params_and_source()
    {
        var survey = new LpzSurvey();
        survey.Add(BuildProgram(withArchive: true));
        Assert.True(!survey.Types.ContainsKey(0), "AND runs, so it isn't listed");
        Assert.True(survey.Types.TryGetValue(999, out var t), "type 999 listed");
        Assert.Equal(2, t!.Count, "two records");
        Assert.Equal(2, t.Examples.Count, "one example per shape");
        var e = t.Examples[0];
        Assert.Equal("press_a", e.Inputs[0], "input named from .sig");
        Assert.Equal("1", e.Inputs[1], "constant shown as 1");
        Assert.Equal("toggle_fb", e.Outputs[0], "output named");
        Assert.True(e.Params.Contains("250") && e.Params.Contains("\"abc\"") && e.Params.Contains("f44=7"), "params and other fields: " + string.Join(",", e.Params));
        Assert.True(e.Source != null && e.Source.StartsWith("Toggle (SmC 0"), "matched to the source symbol driving toggle_fb: " + e.Source);
        Assert.Equal("::inner", t.Examples[1].Outputs[0], "module-internal name shortened");
        Assert.Equal("#15", t.Examples[1].Outputs[1], "unnamed signal shown by index");

        var report = survey.Report();
        Assert.True(report.Contains("== type 999 (unidentified): 2 record(s) in 1 program(s)"), report);
        Assert.True(report.Contains("Toggle (SmC 0") && report.Contains("×1"), "source match summary");
    }

    [Test]
    public static void Survey_uses_sig_next_to_the_lpz_and_report_includes_it()
    {
        var path = BuildProgram(withArchive: false);
        var survey = new LpzSurvey();
        survey.Add(path);
        Assert.Equal("press_a", survey.Types[999].Examples[0].Inputs[0], ".sig beside the .lpz");

        var report = LpzInspect.Report(path);
        Assert.True(report.Contains("Runnable today: 2 of 4"), "header and AND count as runnable, 999 doesn't:\n" + report);
        Assert.True(report.Contains("== type 999 (unidentified)"), "inspect report lists examples");
    }

    [Test]
    public static void Catalog_counts_runtime_built_types_as_running()
    {
        foreach (var t in new[] { 213, 214, 215, 268, -200, -242, -310, 4, 7, 20, 205, 266 })
            Assert.True(PrimitiveCatalog.Known.TryGetValue(t, out var k) && k.Implemented, $"type {t} known and runs");
    }
}
