namespace OpenVP.Core;

/// <summary>One difference between two traces.</summary>
/// <param name="Kind"><c>onlyA</c> or <c>onlyB</c> (a change one trace has and the other doesn't), <c>value</c> (the same step has another value) or <c>timing</c> (same change, but at times further apart than the tolerance).</param>
/// <param name="OffsetMs">Where it sits on the timeline, ms after the trace's first change (the earlier of the two sides).</param>
/// <param name="DeltaMs">For <c>timing</c>: B's time minus A's, ms.</param>
public sealed record TraceDiff(string Kind, string Signal, TraceRow? A, TraceRow? B, double OffsetMs, double? DeltaMs, string Text);

public sealed record TraceComparison(int RowsA, int RowsB, int Matched, int OnlyA, int OnlyB, int ValueDiffs, int TimingDiffs,
    double ToleranceMs, IReadOnlyList<TraceDiff> Diffs, bool Truncated)
{
    public bool Same => OnlyA == 0 && OnlyB == 0 && ValueDiffs == 0 && TimingDiffs == 0;
}

/// <summary>
/// Compares two traces, signal by signal: the changes of each signal are lined up by value (a longest common
/// subsequence), so one extra change doesn't throw the rest out. What lines up is checked for timing, with times
/// measured from each trace's own first change, so two runs started at different moments can be compared. What
/// doesn't line up is a change only one trace has, or (when both have one in the same place) a different value.
/// </summary>
public static class TraceCompare
{
    private const long MaxCells = 4_000_000; // per signal: n*m of the alignment; longer runs are compared position by position

    public static TraceComparison Compare(IReadOnlyList<TraceRow> a, IReadOnlyList<TraceRow> b, double toleranceMs = 50, int maxDiffs = 5000)
    {
        toleranceMs = Math.Max(0, toleranceMs);
        var t0a = a.Count > 0 ? a.Min(r => r.Time) : DateTimeOffset.MinValue;
        var t0b = b.Count > 0 ? b.Min(r => r.Time) : DateTimeOffset.MinValue;
        var byA = Group(a); var byB = Group(b);
        var diffs = new List<TraceDiff>();
        int matched = 0, onlyA = 0, onlyB = 0, valueDiffs = 0, timingDiffs = 0;
        double OffA(TraceRow r) => (r.Time - t0a).TotalMilliseconds;
        double OffB(TraceRow r) => (r.Time - t0b).TotalMilliseconds;

        foreach (var sig in byA.Keys.Union(byB.Keys, StringComparer.Ordinal))
        {
            var sa = byA.GetValueOrDefault(sig) ?? new List<TraceRow>();
            var sb = byB.GetValueOrDefault(sig) ?? new List<TraceRow>();
            var pairs = Align(sa, sb);   // (indexA, indexB) of matching changes, ascending
            int ia = 0, ib = 0;
            void Gap(int endA, int endB)
            {
                // Between two matches: pair what is left in order as "different value", the rest is only on one side.
                while (ia < endA && ib < endB)
                {
                    var x = sa[ia++]; var y = sb[ib++];
                    valueDiffs++;
                    diffs.Add(new TraceDiff("value", sig, x, y, Math.Min(OffA(x), OffB(y)), null,
                        $"{sig}: A has {x.ValueText} at +{OffA(x) / 1000:0.000}s, B has {y.ValueText} at +{OffB(y) / 1000:0.000}s"));
                }
                while (ia < endA) { var x = sa[ia++]; onlyA++; diffs.Add(new TraceDiff("onlyA", sig, x, null, OffA(x), null, $"{sig} → {x.ValueText} at +{OffA(x) / 1000:0.000}s only in A")); }
                while (ib < endB) { var y = sb[ib++]; onlyB++; diffs.Add(new TraceDiff("onlyB", sig, null, y, OffB(y), null, $"{sig} → {y.ValueText} at +{OffB(y) / 1000:0.000}s only in B")); }
            }
            foreach (var (pa, pb) in pairs)
            {
                Gap(pa, pb);
                var x = sa[pa]; var y = sb[pb];
                ia = pa + 1; ib = pb + 1;
                matched++;
                var d = OffB(y) - OffA(x);
                if (Math.Abs(d) > toleranceMs)
                {
                    timingDiffs++;
                    diffs.Add(new TraceDiff("timing", sig, x, y, Math.Min(OffA(x), OffB(y)), d,
                        $"{sig} → {x.ValueText}: A at +{OffA(x) / 1000:0.000}s, B at +{OffB(y) / 1000:0.000}s ({(d >= 0 ? "+" : "−")}{Math.Abs(d):0} ms)"));
                }
            }
            Gap(sa.Count, sb.Count);
        }
        diffs.Sort((p, q) => p.OffsetMs.CompareTo(q.OffsetMs));
        var truncated = diffs.Count > maxDiffs;
        if (truncated) diffs.RemoveRange(maxDiffs, diffs.Count - maxDiffs);
        return new TraceComparison(a.Count, b.Count, matched, onlyA, onlyB, valueDiffs, timingDiffs, toleranceMs, diffs, truncated);
    }

    private static Dictionary<string, List<TraceRow>> Group(IReadOnlyList<TraceRow> rows)
    {
        var g = new Dictionary<string, List<TraceRow>>(StringComparer.Ordinal);
        foreach (var r in rows.OrderBy(r => r.Time))   // stable: equal times keep their order
        {
            if (!g.TryGetValue(r.Signal, out var l)) g[r.Signal] = l = new List<TraceRow>();
            l.Add(r);
        }
        return g;
    }

    // Longest common subsequence by value text. Returns the matched index pairs.
    private static List<(int, int)> Align(List<TraceRow> a, List<TraceRow> b)
    {
        var res = new List<(int, int)>();
        int n = a.Count, m = b.Count;
        if (n == 0 || m == 0) return res;
        if ((long)n * m > MaxCells)
        {
            for (var i = 0; i < Math.Min(n, m); i++) if (a[i].ValueText == b[i].ValueText) res.Add((i, i));
            return res;
        }
        var av = a.Select(r => r.ValueText).ToArray(); var bv = b.Select(r => r.ValueText).ToArray();
        var L = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                L[i, j] = av[i] == bv[j] ? L[i + 1, j + 1] + 1 : Math.Max(L[i + 1, j], L[i, j + 1]);
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (av[x] == bv[y]) { res.Add((x, y)); x++; y++; }
            else if (L[x + 1, y] >= L[x, y + 1]) x++;
            else y++;
        }
        return res;
    }
}
