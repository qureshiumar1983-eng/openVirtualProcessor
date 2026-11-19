using OpenVP.Core;

namespace OpenVP.Tests;

public static class UnsupportedLogTests
{
    static string TempFile() => Path.Combine(Directory.CreateTempSubdirectory("openvp-unsupported").FullName, "unsupported.md");

    [Test]
    public static void Reports_are_counted_once_per_program_and_saved()
    {
        var path = TempFile();
        using (var log = new UnsupportedLog(path))
        {
            log.Report("SIMPL symbol", "Analog Ramp (SmC 64)", "pins in=3 out=1", "ProgramA");
            log.Report("SIMPL symbol", "Analog Ramp (SmC 64)", "pins in=3 out=1", "ProgramA"); // reloaded: not counted again
            log.Report("SIMPL symbol", "Analog Ramp (SmC 64)", "pins in=3 out=1", "ProgramB");
            log.Report("SIMPL# member", "Crestron.SimplSharp.Foo.Bar", null, "ProgramA");
            Assert.Equal(2, log.Items.Single(i => i.Name.StartsWith("Analog Ramp")).Seen, "two programs");
        }
        var text = File.ReadAllText(path);
        Assert.True(text.Contains("| SIMPL symbol | Analog Ramp (SmC 64) | pins in=3 out=1 | 2 |"), "row written: " + text);
        Assert.True(!text.Contains("ProgramA") && !text.Contains("ProgramB"), "program names never written");

        // Next run picks the file up and keeps counting.
        using (var again = new UnsupportedLog(path))
        {
            Assert.Equal(2, again.Items.Count, "both rows reloaded");
            again.Report("SIMPL symbol", "Analog Ramp (SmC 64)", null, "ProgramA");
            Assert.Equal(3, again.Items.Single(i => i.Kind == "SIMPL symbol").Seen, "counted in the new run");
            Assert.Equal("pins in=3 out=1", again.Items.Single(i => i.Kind == "SIMPL symbol").Detail, "detail kept");
        }
    }

    [Test]
    public static void Parse_survives_pipes_and_merges_duplicate_rows()
    {
        var log = new UnsupportedLog();
        log.Report("SIMPL+ module", "x.dll", "a | b");
        var rows = UnsupportedLog.Parse(log.Render().Split('\n')).ToList();
        Assert.Equal("a | b", rows.Single().Detail, "escaped pipe round-trips");

        // The same row twice (e.g. both sides of a merge kept): counts add up.
        var merged = UnsupportedLog.Parse(new[]
        {
            "| SIMPL symbol | X (SmC 1) |  | 2 | 2026-01-05 | 2026-02-01 |",
            "<<<<<<< HEAD",
            "| SIMPL symbol | X (SmC 1) |  | 3 | 2026-01-01 | 2026-01-20 |",
        }).Single();
        Assert.Equal(5, merged.Seen, "seen added");
        Assert.Equal(new DateOnly(2026, 1, 1), merged.FirstSeen, "earliest first seen");
        Assert.Equal(new DateOnly(2026, 2, 1), merged.LastSeen, "latest last seen");
    }
}
