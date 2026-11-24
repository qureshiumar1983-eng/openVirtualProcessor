using System.Text.Json;
using OpenVP.Core;
using OpenVP.Simpl;

namespace OpenVP.Tests;

/// <summary>
/// Plays openvp-logic-vectors.json against OpenVP_Logic_Test.smw on a fresh OpenVP processor and reports, per test,
/// whether every expectation held. Each test is independent of the others (its own signals), so they share one load.
/// </summary>
public static class LogicVectors
{
    /// <summary>Status: PASS, FAIL, or UNSUPPORTED (OpenVP has no behavior for the symbol, so it cannot be right).</summary>
    public sealed record Result(string Id, string Symbol, int Smc, string Confidence, string Status, string Detail)
    {
        public bool Passed => Status == "PASS";
    }

    public static List<Result> Run(string smwPath, string vectorsPath, Action<string>? log = null, string? only = null)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(vectorsPath));
        var root = doc.RootElement;
        var types = root.GetProperty("signal_types").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!, StringComparer.OrdinalIgnoreCase);

        var dir = Directory.CreateTempSubdirectory("openvp-logic").FullName;
        var path = Path.Combine(dir, Path.GetFileName(smwPath));
        File.Copy(smwPath, path);
        using var p = new Processor(dir, 1);
        p.AddLoader(new SimplLoader());
        p.LoadProgram(1, path);
        var slot = p.Slot(1);
        if (slot.State != SlotState.Running) throw new InvalidOperationException("program did not start: " + slot.LastError);
        var rt = slot.Runtime!;
        void Settle() { for (int i = 0; i < 3; i++) slot.Dispatcher.Invoke(() => { }); }
        Settle();
        var unsupported = new HashSet<int>();
        if (rt is SimplRuntime sr) foreach (var u in sr.Unsupported) unsupported.Add(u.Code);

        Dictionary<string, object> Snapshot()
        {
            Settle();
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in rt.GetSignals()) d[s.Name] = s.Value;
            return d;
        }

        object Convert(string name, JsonElement v) => types.GetValueOrDefault(name) switch
        {
            "d" => v.ValueKind == JsonValueKind.True,
            "a" => (ushort)v.GetInt32(),
            "s" => v.GetString() ?? "",
            _ => throw new InvalidOperationException($"unknown signal {name}")
        };

        static string Show(object? o) => o switch { null => "(missing)", string s => JsonSerializer.Serialize(s), bool b => b ? "1" : "0", _ => o.ToString()! };

        string? Mismatch(Dictionary<string, object> snap, JsonElement expect)
        {
            var bad = new List<string>();
            foreach (var e in expect.EnumerateObject())
            {
                var want = Convert(e.Name, e.Value);
                snap.TryGetValue(e.Name, out var got);
                if (!Equals(want, got)) bad.Add($"{e.Name}: expected {Show(want)}, got {Show(got)}");
            }
            if (bad.Count == 0) return null;
            return string.Join("; ", bad.Take(4)) + (bad.Count > 4 ? $" (+{bad.Count - 4} more)" : "");
        }

        var results = new List<Result>();
        foreach (var t in root.GetProperty("tests").EnumerateArray())
        {
            var id = t.GetProperty("id").GetString()!;
            if (only != null && !id.Equals(only, StringComparison.OrdinalIgnoreCase)) continue;
            var sym = t.GetProperty("symbol").GetString()!;
            var smc = t.GetProperty("smc").GetInt32();
            var conf = t.GetProperty("confidence").GetString()!;
            string? fail = null;
            int n = 0;
            foreach (var step in t.GetProperty("steps").EnumerateArray())
            {
                n++;
                if (step.TryGetProperty("set", out var set))
                {
                    foreach (var s in set.EnumerateObject())
                        if (!rt.TrySetSignal(s.Name, Convert(s.Name, s.Value))) { fail = $"step {n}: no signal {s.Name}"; break; }
                    Settle();
                }
                else if (step.TryGetProperty("wait_ms", out var w)) { Thread.Sleep(w.GetInt32()); }
                else if (step.TryGetProperty("expect", out var exp)) { var m = Mismatch(Snapshot(), exp); if (m != null) fail = $"step {n}: {m}"; }
                else if (step.TryGetProperty("eventually", out var evn))
                {
                    var end = Environment.TickCount64 + step.GetProperty("timeout_ms").GetInt32();
                    string? m;
                    while ((m = Mismatch(Snapshot(), evn)) != null && Environment.TickCount64 < end) Thread.Sleep(20);
                    if (m != null) fail = $"step {n} (after {step.GetProperty("timeout_ms").GetInt32()} ms): {m}";
                }
                else if (step.TryGetProperty("expect_count_high", out var list))
                {
                    var snap = Snapshot();
                    var want = step.GetProperty("value").GetInt32();
                    var got = list.EnumerateArray().Count(x => snap.TryGetValue(x.GetString()!, out var v) && v is true);
                    if (got != want) fail = $"step {n}: expected {want} high, got {got}";
                }
                if (fail != null) break;
            }
                        var status = unsupported.Contains(smc) ? "UNSUPPORTED" : fail == null ? "PASS" : "FAIL";
            var detail = status == "UNSUPPORTED" ? "OpenVP has no behavior for this symbol (its outputs never change)" + (fail != null ? "; " + fail : "") : fail ?? "ok";
            var r = new Result(id, sym, smc, conf, status, detail);
            results.Add(r);
            log?.Invoke($"{status,-11} {id,-5} {sym} [{conf}]{(r.Passed ? "" : "  " + detail)}");
        }
        return results;
    }
}

/// <summary>
/// CI for the logic symbols: tests/OpenVP.Tests/logic-test/OpenVP_Logic_Test.smw is a SIMPL Windows program that uses every
/// logic symbol found in real programs, each cloned from the largest real instance (so the pin counts and groups are ones
/// SIMPL Windows wrote), and openvp-logic-vectors.json says what every output must be after each input change. Behavior,
/// not OpenVP's current output, is the reference: a failure here is a symbol OpenVP gets wrong (or does not have).
/// Set OPENVP_LOGIC_REPORT to a file path to also write the per-symbol results as JSON.
/// </summary>
public static class LogicTestProgramTests
{
    /// <summary>Symbols not implemented yet, because how they behave isn't known well enough: Double-Precision Analog
    /// Variable Preset (636, a fade engine), Ramp In Progress (566) and Astronomical Clock (438).</summary>
    private static readonly HashSet<string> KnownGaps = ["T28", "T47", "T95"];

    [Test]
    public static void Logic_test_program_matches_its_vectors()
    {
        var dir = Path.Combine(Paths.RepoRoot, "tests", "OpenVP.Tests", "logic-test");
        var results = LogicVectors.Run(Path.Combine(dir, "OpenVP_Logic_Test.smw"), Path.Combine(dir, "openvp-logic-vectors.json"), Console.WriteLine);
        if (Environment.GetEnvironmentVariable("OPENVP_LOGIC_REPORT") is { Length: > 0 } report)
            File.WriteAllText(report, System.Text.Json.JsonSerializer.Serialize(results, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        // "unverified" tests are guesses still to be checked on a real processor: reported above, never a failure.
        // Known gaps are symbols OpenVP doesn't run yet: reported, not a failure, until someone implements them (then
        // take them off this list so they stay right).
        var bad = results.Where(r => !r.Passed && r.Confidence != "unverified" && !(KnownGaps.Contains(r.Id) && r.Status == "UNSUPPORTED")).ToList();
        Assert.True(bad.Count == 0, $"{bad.Count} of {results.Count} logic symbol tests did not pass:\n" +
            string.Join("\n", bad.Select(r => $"  {r.Status} {r.Id} {r.Symbol} (SmC {r.Smc}, {r.Confidence}): {r.Detail}")));
    }
}
