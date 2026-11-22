using System.IO.Compression;
using System.Text;
using OpenVP.Core;
using OpenVP.Lpz;
using OpenVP.Server;
using static OpenVP.Tests.LpzTests;

namespace OpenVP.Tests;

/// <summary>"Will it run?" before loading, the program summary, and the starter projects.</summary>
public static class ReadinessTests
{
    static string Temp() => Directory.CreateTempSubdirectory("openvp-ready").FullName;
    static ReadinessChecker Checker() => new(Temp());
    static string SampleSmw => Path.Combine(Paths.RepoRoot, "samples", "SimplWindows", "SampleProgram.smw");

    // ---- verdicts

    [Test]
    public static void No_problems_means_will_run_and_a_few_mean_will_partly_run()
    {
        var ok = Readiness.Evaluate(Array.Empty<ReadinessProblem>());
        Assert.Equal(ReadinessLevel.WillRun, ok.Level, "level");
        Assert.Equal("Will run", ok.Headline, "headline");
        Assert.Equal("will-run", ok.Code, "code");

        var one = Readiness.Evaluate(new[] { new ReadinessProblem("SIMPL symbol", "Analog Ramp") });
        Assert.Equal(ReadinessLevel.PartlyRuns, one.Level, "one problem");
        Assert.Equal("Will partly run — 1 thing isn't supported yet", one.Headline, "singular headline");

        var three = Readiness.Evaluate(new[] { new ReadinessProblem("a", "x"), new ReadinessProblem("a", "x"), new ReadinessProblem("b", "y"), new ReadinessProblem("b", "z") });
        Assert.Equal("Will partly run — 3 things aren't supported yet", three.Headline, "the same thing reported twice counts once");
        Assert.Equal(3, three.Problems.Count, "listed");
        Assert.Equal("partly", three.Code, "code");
    }

    [Test]
    public static void A_load_error_or_a_blocker_means_wont_run_with_the_reason()
    {
        var a = Readiness.Evaluate(Array.Empty<ReadinessProblem>(), loadError: "bad program");
        Assert.Equal(ReadinessLevel.WontRun, a.Level, "load error");
        Assert.Equal("bad program", a.Reason, "reason");
        Assert.Equal("Won't run", a.Headline, "headline");
        var b = Readiness.Evaluate(new[] { new ReadinessProblem("a", "x") }, blocker: "no entry class");
        Assert.Equal(ReadinessLevel.WontRun, b.Level, "a blocker beats a list of problems");
        Assert.Equal("wont-run", b.Code, "code");
        Assert.True(Readiness.UnsupportedFile("notes.txt").Reason!.Contains(".txt"), "names the file type");
        Assert.True(Readiness.Explain(new BadImageFormatException()).Contains(".NET"), "a bad image says what is wrong");
    }

    // ---- the checker, on real files

    [Test]
    public static void The_sample_programs_will_run_and_loading_them_for_the_check_touches_nothing_else()
    {
        var checker = Checker();
        var sharp = checker.Check(Paths.SampleDll);
        Assert.Equal(ReadinessLevel.WillRun, sharp.Verdict.Level, "SIMPL# sample: " + sharp.Verdict.Headline + " " + string.Join("; ", sharp.Verdict.Problems.Select(p => p.Name)));
        Assert.Equal("SIMPL#", sharp.Kind, "kind");
        Assert.True(sharp.Summary != null && !sharp.Summary.DevicesKnown && sharp.Summary.Uses!.Contains("XpanelForHtml5"), "a SIMPL# program that isn't running names its device types");

        var smw = checker.Check(SampleSmw);
        Assert.Equal(ReadinessLevel.WillRun, smw.Verdict.Level, "SIMPL Windows sample: " + smw.Verdict.Headline + " " + string.Join("; ", smw.Verdict.Problems.Select(p => p.Name)));
        Assert.True(smw.Summary!.Text.StartsWith("SIMPL Windows · "), "summary text: " + smw.Summary.Text);
        Assert.True(smw.Summary.PanelIpIds.Count == 1 && smw.Summary.EiscIpIds.Count == 1, "one panel and one EISC: " + smw.Summary.Text);
    }

    [Test]
    public static void A_bogus_file_wont_run_and_says_why()
    {
        var dir = Temp();
        var checker = Checker();
        var txt = Path.Combine(dir, "notes.txt"); File.WriteAllText(txt, "hello");
        var r = checker.Check(txt);
        Assert.Equal(ReadinessLevel.WontRun, r.Verdict.Level, "a .txt");
        Assert.True(r.Verdict.Reason!.Contains(".txt"), "reason: " + r.Verdict.Reason);

        foreach (var ext in new[] { ".dll", ".lpz", ".cpz", ".smw" })
        {
            var f = Path.Combine(dir, "bogus" + ext); File.WriteAllText(f, "this is not a program");
            var res = checker.Check(f);
            Assert.Equal(ReadinessLevel.WontRun, res.Verdict.Level, $"bogus {ext}: {res.Verdict.Headline}");
            Assert.True(!string.IsNullOrWhiteSpace(res.Verdict.Reason), $"bogus {ext} has a reason");
        }
    }

    [Test]
    public static void A_dll_with_no_control_system_class_wont_run()
    {
        // OpenVP.Core.dll is a real .NET assembly, just not a program.
        var dir = Temp();
        var f = Path.Combine(dir, "NotAProgram.dll");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "OpenVP.Core.dll"), f);
        var r = Checker().Check(f);
        Assert.Equal(ReadinessLevel.WontRun, r.Verdict.Level, "no entry class: " + r.Verdict.Headline);
        Assert.True(r.Verdict.Reason!.Contains("CrestronControlSystem"), "says what is missing: " + r.Verdict.Reason);
    }

    static string BuildLpz(string path, IEnumerable<byte[]> records)
    {
        var bin = new List<byte>();
        bin.AddRange(Record(31, "LeadingREM", [], [], Int(15, 1), Str(3, "Program File: ready.smw")));
        foreach (var r in records) bin.AddRange(r);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Put(string name, byte[] content) { using var s = zip.CreateEntry(name).Open(); s.Write(content); }
        Put("boot.bt", Encoding.ASCII.GetBytes("[LG_BOOT_DATA]\r\nBinFileName=ready.bin\r\nSMWFileName=ready.smw\r\nRackType=CP4\r\n"));
        Put("ready.bin", bin.ToArray());
        Put("ready.dip", Encoding.ASCII.GetBytes("[IPTable]\r\nid0=03\r\naddr0=127.0.0.1\r\nid1=10\r\naddr1=127.0.0.2\r\n"));
        Put("ready.dsc", Encoding.ASCII.GetBytes("Slot : 2.IP-ID : 03;3;Touchpanels(Enet);TSW-1070;;TSW-1070;-12\r\n"));
        return path;
    }

    [Test]
    public static void A_compiled_program_with_an_unknown_record_will_partly_run_and_lists_it()
    {
        var f = BuildLpz(Path.Combine(Temp(), "partly.lpz"), new[]
        {
            Record(-12, "IP-ID-03", [20], [], Int(6, 3), Int(10, 1), Int(11, 0), Int(12, 1), Int(13, 0)),
            Record(9999, "S-1", [20], [21], Int(15, 1)),
        });
        var r = Checker().Check(f);
        Assert.Equal(ReadinessLevel.PartlyRuns, r.Verdict.Level, "level: " + r.Verdict.Headline);
        Assert.Equal("Will partly run — 1 thing isn't supported yet", r.Verdict.Headline, "headline");
        Assert.True(r.Verdict.Problems.Any(p => p.Kind == "Compiled primitive" && p.Name == "type 9999"), "lists the unknown type: " + string.Join("; ", r.Verdict.Problems.Select(p => p.Name)));
    }

    [Test]
    public static void A_compiled_program_that_is_fully_supported_will_run()
    {
        var f = BuildLpz(Path.Combine(Temp(), "fine.lpz"), new[]
        {
            Record(-12, "IP-ID-03", [20], [], Int(6, 3), Int(10, 1), Int(11, 0), Int(12, 1), Int(13, 0)),
        });
        var r = Checker().Check(f);
        Assert.Equal(ReadinessLevel.WillRun, r.Verdict.Level, "level: " + r.Verdict.Headline);
    }

    // ---- the summary

    [Test]
    public static void The_summary_sentence_reads_like_the_example()
    {
        var s = new ProgramSummary("SIMPL Windows", ["03", "04", "05"], ["10", "11"], 0, 4, 1, 12, 2, 1284);
        Assert.Equal("SIMPL Windows · 3 touch panels (IP IDs 03, 04, 05), 2 EISCs, 4 COM ports, 1 IR port, 12 SIMPL+ modules, 2 user macros, 1,284 signals.", s.Text, "text");
        var one = new ProgramSummary("SIMPL Windows", ["03"], ["10"], 1, 1, 0, 1, 1, 1);
        Assert.Equal("SIMPL Windows · 1 touch panel (IP ID 03), 1 EISC, 1 other IP device, 1 COM port, 1 SIMPL+ module, 1 user macro, 1 signal.", one.Text, "singular");
        Assert.Equal("SIMPL Windows · nothing wired yet.", new ProgramSummary("SIMPL Windows", [], [], 0, 0, 0, 0, 0, 0).Text, "empty");
        Assert.Equal("SIMPL# Pro · 1 touch panel (IP ID 03), 1 EISC, 12 signals.", new ProgramSummary("SIMPL#", ["03"], ["10"], 0, 0, 0, 0, 0, 12).Text, "SIMPL# once running");
        var pre = new ProgramSummary("SIMPL#", [], [], 0, 0, 0, 0, 0, 0, DevicesKnown: false, Uses: ["XpanelForHtml5"]);
        Assert.Equal("SIMPL# Pro · uses XpanelForHtml5. Its devices appear when it starts.", pre.Text, "SIMPL# before it starts");
    }

    [Test]
    public static void Devices_are_told_apart_by_name()
    {
        Assert.Equal(DeviceClass.Panel, DeviceKinds.Classify("TSW-1070"), "TSW");
        Assert.Equal(DeviceClass.Panel, DeviceKinds.Classify("TS-1080"), "TS");
        Assert.Equal(DeviceClass.Panel, DeviceKinds.Classify("XPanel 2.0 Smart Graphics"), "XPanel");
        Assert.Equal(DeviceClass.Panel, DeviceKinds.Classify("XpanelForHtml5"), "HTML5 XPanel");
        Assert.Equal(DeviceClass.Eisc, DeviceKinds.Classify("Ethernet Intersystem Communications"), "EISC");
        Assert.Equal(DeviceClass.Eisc, DeviceKinds.Classify("ThreeSeriesTcpIpEthernetIntersystemCommunications"), "SIMPL# EISC");
        Assert.Equal(DeviceClass.Eisc, DeviceKinds.Classify("IP device", "127.0.0.2"), "a link to another slot");
        Assert.Equal(DeviceClass.Other, DeviceKinds.Classify("DM-NVX-350"), "NVX");
        Assert.Equal(DeviceClass.Other, DeviceKinds.Classify(null), "unknown");
    }

    [Test]
    public static void A_compiled_programs_summary_counts_what_it_has()
    {
        var f = BuildLpz(Path.Combine(Temp(), "sum.lpz"), new[]
        {
            Record(-12, "IP-ID-03", [20], [], Int(6, 3), Int(10, 1), Int(11, 0), Int(12, 1), Int(13, 0)),
            Record(-12, "IP-ID-10", [21], [22], Int(6, 16), Int(10, 1), Int(11, 1), Int(12, 1), Int(13, 1)),
            Record(-12, "IP-ID-31", [], [], Int(6, 49), Int(10, 0), Int(11, 0), Int(12, 0), Int(13, 0)),
            Record(-201, "Slot-08", [], [], Int(6, 8), Int(7, 3)),
            Record(-200, "COM-01", [23, 1], [24], Int(15, 2), Int(6, 0), Int(7, 3)),
            Record(-200, "COM-02", [25, 1], [26], Int(15, 2), Int(6, 1), Int(7, 3)),
            Record(9999, "S-2.1:S-5", [20], [27], Int(15, 1)),
            Record(9999, "S-2.1:S-6", [27], [28], Int(15, 1)),
            Record(9999, "S-2.2:S-5", [28], [29], Int(15, 1)),
        });
        var p = new Processor(Temp(), 1);
        using var _ = p;
        p.AddLoader(new LpzLoader());
        p.LoadProgram(1, f, start: false);
        var s = ((IProgramAnalysis)p.Slot(1).Runtime!).Summarize();
        Assert.Equal("SIMPL Windows", s.Kind, "kind");
        Assert.Equal("03", string.Join(",", s.PanelIpIds), "panel by its model (TSW-1070) from the .dsc");
        Assert.Equal("10", string.Join(",", s.EiscIpIds), "EISC by its 127.0.0.2 address");
        Assert.Equal(1, s.OtherDevices, "the third IP device has no model");
        Assert.Equal(2, s.ComPorts, "COM ports (" + s.Text + ")");
        Assert.Equal(2, s.UserMacros, "two macro scopes");
        Assert.Equal(10, s.Signals, "signals 20 to 29, whether or not OpenVP can run the records that use them");
        Assert.True(s.Text.Contains("1 touch panel (IP ID 03), 1 EISC, 1 other IP device, 2 COM ports"), "text: " + s.Text);
    }

    [Test]
    public static void A_running_simpl_sharp_programs_summary_comes_from_its_devices()
    {
        using var p = new Processor(Temp(), 1);
        p.AddLoader(new OpenVP.SimplSharpHost.SimplSharpLoader());
        p.LoadProgram(1, Paths.SampleDll);
        var a = (IProgramAnalysis)p.Slot(1).Runtime!;
        var s = a.Summarize();
        Assert.True(s.DevicesKnown, "devices registered when it started");
        Assert.Equal("03", string.Join(",", s.PanelIpIds), "panel");
        Assert.Equal("10", string.Join(",", s.EiscIpIds), "EISC");
        Assert.True(s.Text.StartsWith("SIMPL# Pro · 1 touch panel (IP ID 03), 1 EISC"), s.Text);
    }

    // ---- starter projects

    [Test]
    public static void The_ch5_starter_is_a_valid_ch5z_that_installs_as_a_panel_project()
    {
        var bytes = Templates.Default().Ch5Zip();
        using var zip = new ZipArchive(new MemoryStream(bytes));
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.True(names.Contains("index.html") && names.Contains("app.js") && names.Contains("style.css"), "files at the top: " + string.Join(", ", names));
        var dir = Temp();
        var info = new Ch5Panels(dir).Install(1, 3, new MemoryStream(bytes), "starter.ch5z");
        Assert.True(info.Files >= 3, "installed");
        var app = new StreamReader(zip.GetEntry("app.js")!.Open()).ReadToEnd();
        Assert.True(app.Contains("publishEvent(\"b\"") && app.Contains("subscribeState(\"b\"") && app.Contains("\"n\"") && app.Contains("\"s\""), "uses numeric joins of all three kinds");
    }

    [Test]
    public static void The_simpl_sharp_starter_zip_has_the_project_the_source_and_the_api_assemblies()
    {
        var bytes = Templates.Default().SimplSharpZip();
        using var zip = new ZipArchive(new MemoryStream(bytes));
        var names = zip.Entries.Select(e => e.FullName).ToHashSet();
        foreach (var need in new[] { "StarterProgram/StarterProgram.csproj", "StarterProgram/ControlSystem.cs", "StarterProgram/README.txt", "StarterProgram/lib/SimplSharpPro.dll", "StarterProgram/lib/OpenVP.Core.dll" })
            Assert.True(names.Contains(need), "has " + need);
        var cs = new StreamReader(zip.GetEntry("StarterProgram/ControlSystem.cs")!.Open()).ReadToEnd();
        Assert.True(cs.Contains("XpanelForHtml5(0x03") && cs.Contains("IntersystemCommunications(0x10"), "a panel at IP ID 03 and an EISC");
    }

    [Test]
    public static void An_unknown_template_is_not_found()
    {
        Assert.True(Templates.Default().Build("nope") == null, "no such template");
        Assert.True(!Templates.Default().Available("nope"), "not available");
    }
}
