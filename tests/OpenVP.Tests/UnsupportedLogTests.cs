using OpenVP.Core;
using OpenVP.Lpz;

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

    [Test]
    public static void Supported_symbols_are_recognised_by_name_code_and_type()
    {
        Assert.True(SupportedSymbols.IsSupported("SIMPL symbol", "Analog Multiplier (SmC 99999)"), "a catalog name, whatever the code");
        Assert.True(SupportedSymbols.IsSupported("SIMPL symbol", "Some Renamed Thing (SmC 1)"), "a catalog SmC code, whatever the name (AND)");
        Assert.True(SupportedSymbols.IsSupported("Compiled primitive", "type 0"), "an identified type");
        Assert.True(!SupportedSymbols.IsSupported("Compiled primitive", "type 107"), "an unidentified type");
        Assert.True(!SupportedSymbols.IsSupported("SIMPL symbol", "press and hold v1.2.cmc (SmC 155)"), "a user macro is not a catalog symbol");
        Assert.True(!SupportedSymbols.IsSupported("SIMPL symbol", "Display Helper.usp (SmC 103)"), "a SIMPL+ module is not either");
        Assert.True(!SupportedSymbols.IsSupported("SIMPL hardware", "AND (SmC 1)"), "other kinds are never filtered");
        Assert.True(!SupportedSymbols.IsSupported("SIMPL symbol", "no code here"), "a row in another shape is kept");
    }

    [Test]
    public static void Rows_for_symbols_supported_since_are_dropped_from_the_file_and_not_reported_again()
    {
        var path = TempFile();
        File.WriteAllText(path,
            "| Kind | Symbol / API | Details | Programs | First seen | Last seen |\n|---|---|---|---:|---|---|\n" +
            "| SIMPL symbol | Analog Multiplier (SmC 4242) | pins in=2 out=1 | 3 | 2026-11-19 | 2026-11-19 |\n" +
            "| SIMPL symbol | Mystery Thing (SmC 4243) | pins in=2 out=1 | 2 | 2026-11-19 | 2026-11-19 |\n" +
            "| Compiled primitive | type 0 |  | 1 | 2026-11-19 | 2026-11-19 |\n");
        using (var log = new UnsupportedLog())
        {
            log.SetSupportCheck(SupportedSymbols.IsSupported);
            log.UseFile(path);
            Assert.Equal(1, log.Items.Count, "only the symbol OpenVP still lacks is kept");
            log.Report("SIMPL symbol", "Serial Compare (SmC 4244)", null, "ProgramA");
            Assert.Equal(1, log.Items.Count, "a supported symbol is not reported again");
            log.Flush();
        }
        var text = File.ReadAllText(path);
        Assert.True(text.Contains("Mystery Thing") && !text.Contains("Analog Multiplier") && !text.Contains("type 0"), "the file was rewritten without them: " + text);

        // Turning the check on for rows already held prunes them.
        using var late = new UnsupportedLog();
        late.Report("SIMPL symbol", "Time Split (SmC 1)", null, "P");
        late.Report("SIMPL symbol", "Mystery Thing (SmC 4243)", null, "P");
        late.SetSupportCheck(SupportedSymbols.IsSupported);
        Assert.Equal("Mystery Thing (SmC 4243)", late.Items.Single().Name, "pruned at once");
    }
}
