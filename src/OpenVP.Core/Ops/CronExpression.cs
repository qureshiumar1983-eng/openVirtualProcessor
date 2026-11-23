namespace OpenVP.Core.Ops;

/// <summary>
/// A five-field cron expression (<c>minute hour day-of-month month day-of-week</c>) that finds its next run in a time zone's wall clock, with
/// daylight saving handled the way cron does:
/// <list type="bullet">
/// <item>A wall-clock time that doesn't exist (the hour skipped when the clocks go forward) runs at the first moment after the gap, once. <c>30 2 * * *</c> runs at 03:00 on that day.</item>
/// <item>A wall-clock time that happens twice (the hour repeated when the clocks go back) runs once, the first time, when the hour field names an hour (<c>30 1 * * *</c>).
/// When the hour field is a wildcard (<c>0 * * * *</c>, <c>*/15 * * * *</c>) it runs at every matching moment, so an hourly job still runs every hour.</item>
/// </list>
/// Supports lists (<c>1,15</c>), ranges (<c>1-5</c>), steps (<c>*/10</c>, <c>0-30/5</c>), month names (<c>jan</c>) and day names (<c>mon-fri</c>); day-of-week 0 and 7 are Sunday.
/// As in cron, when both day-of-month and day-of-week are restricted a day matches if either does. Also <c>@hourly</c>, <c>@daily</c>, <c>@weekly</c>, <c>@monthly</c>, <c>@yearly</c>.
/// </summary>
public sealed class CronExpression
{
    private readonly bool[] _min = new bool[60], _hour = new bool[24], _dom = new bool[32], _mon = new bool[13], _dow = new bool[7];
    private readonly bool _domStar, _dowStar, _hourWild;

    public string Text { get; }

    private CronExpression(string text, string[] f)
    {
        Text = text;
        Fill(f[0], _min, 0, 59, null, "minute");
        Fill(f[1], _hour, 0, 23, null, "hour");
        Fill(f[2], _dom, 1, 31, null, "day of month");
        Fill(f[3], _mon, 1, 12, Months, "month");
        var dowRaw = new bool[8];
        Fill(f[4], dowRaw, 0, 7, Days, "day of week");
        for (var i = 0; i < 7; i++) _dow[i] = dowRaw[i] || (i == 0 && dowRaw[7]);
        _domStar = f[2].StartsWith('*');
        _dowStar = f[4].StartsWith('*');
        _hourWild = f[1].StartsWith('*');
    }

    private static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];
    private static readonly string[] Days = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    /// <summary>Reads an expression. Throws <see cref="FormatException"/> with a plain message that says which field is wrong.</summary>
    public static CronExpression Parse(string? text)
    {
        var t = (text ?? "").Trim();
        if (t.Length == 0) throw new FormatException("Enter a cron expression such as 30 2 * * * (minute hour day-of-month month day-of-week).");
        var expanded = t.ToLowerInvariant() switch
        {
            "@hourly" => "0 * * * *",
            "@daily" or "@midnight" => "0 0 * * *",
            "@weekly" => "0 0 * * 0",
            "@monthly" => "0 0 1 * *",
            "@yearly" or "@annually" => "0 0 1 1 *",
            _ => t
        };
        var f = expanded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (f.Length != 5) throw new FormatException($"A cron expression has 5 fields (minute hour day-of-month month day-of-week), but this has {f.Length}.");
        return new CronExpression(t, f);
    }

    public static bool TryParse(string? text, out CronExpression? cron, out string? error)
    {
        try { cron = Parse(text); error = null; return true; }
        catch (FormatException e) { cron = null; error = e.Message; return false; }
    }

    private static void Fill(string field, bool[] set, int lo, int hi, string[]? names, string what)
    {
        foreach (var part in field.Split(','))
        {
            if (part.Length == 0) throw new FormatException($"The {what} field has an empty item.");
            var step = 1;
            var range = part;
            var slash = part.IndexOf('/');
            if (slash >= 0)
            {
                if (!int.TryParse(part[(slash + 1)..], out step) || step < 1) throw new FormatException($"The {what} field has a bad step in '{part}'.");
                range = part[..slash];
            }
            int from, to;
            if (range == "*") { from = lo; to = hi; if (what == "day of week") to = 6; }
            else if (range.Contains('-'))
            {
                var ab = range.Split('-');
                if (ab.Length != 2) throw new FormatException($"The {what} field has a bad range '{range}'.");
                from = Num(ab[0], lo, hi, names, what); to = Num(ab[1], lo, hi, names, what);
                if (from > to) throw new FormatException($"The {what} field has a range that runs backwards: '{range}'.");
            }
            else
            {
                from = Num(range, lo, hi, names, what);
                to = slash >= 0 ? hi : from;
            }
            for (var v = from; v <= to; v += step) set[v] = true;
        }
    }

    private static int Num(string s, int lo, int hi, string[]? names, string what)
    {
        if (names != null)
        {
            var i = Array.IndexOf(names, s.ToLowerInvariant());
            if (i >= 0) return what == "month" ? i + 1 : i;
        }
        if (!int.TryParse(s, out var v)) throw new FormatException($"The {what} field has '{s}', which isn't a number{(names != null ? " or a name" : "")}.");
        if (v < lo || v > hi) throw new FormatException($"The {what} field must be {lo} to {hi}, not {v}.");
        return v;
    }

    private bool DayMatches(DateTime d)
    {
        if (!_mon[d.Month]) return false;
        var dom = _dom[d.Day];
        var dow = _dow[(int)d.DayOfWeek];
        if (!_domStar && !_dowStar) return dom || dow;
        if (!_domStar) return dom;
        if (!_dowStar) return dow;
        return true;
    }

    /// <summary>The first run strictly after <paramref name="after"/>, or null if there is none in the next five years.</summary>
    public DateTimeOffset? Next(DateTimeOffset after, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(after, zone);
        var start = local.Date;
        for (var day = 0; day < 366 * 5; day++)
        {
            var date = start.AddDays(day);
            if (!DayMatches(date)) continue;
            var found = Instants(date, zone).Where(i => i > after).OrderBy(i => i).Select(i => (DateTimeOffset?)i).FirstOrDefault();
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>The next <paramref name="count"/> runs after <paramref name="after"/>.</summary>
    public List<DateTimeOffset> Upcoming(DateTimeOffset after, TimeZoneInfo zone, int count)
    {
        var list = new List<DateTimeOffset>();
        var t = after;
        while (list.Count < count && Next(t, zone) is { } n) { list.Add(n); t = n; }
        return list;
    }

    // Every moment on this date (a wall-clock date) that matches, as UTC-based instants.
    private IEnumerable<DateTimeOffset> Instants(DateTime date, TimeZoneInfo zone)
    {
        var seen = new HashSet<long>();
        for (var h = 0; h < 24; h++)
        {
            if (!_hour[h]) continue;
            for (var m = 0; m < 60; m++)
            {
                if (!_min[m]) continue;
                var wall = DateTime.SpecifyKind(date.AddHours(h).AddMinutes(m), DateTimeKind.Unspecified);
                foreach (var i in Resolve(wall, zone))
                    if (seen.Add(i.UtcTicks)) yield return i;
            }
        }
    }

    private IEnumerable<DateTimeOffset> Resolve(DateTime wall, TimeZoneInfo zone)
    {
        if (zone.IsInvalidTime(wall))
        {
            yield return GapEnd(wall, zone);
            yield break;
        }
        if (zone.IsAmbiguousTime(wall))
        {
            var instants = zone.GetAmbiguousTimeOffsets(wall).Select(o => new DateTimeOffset(wall, o).ToUniversalTime()).OrderBy(i => i).ToList();
            if (_hourWild) foreach (var i in instants) yield return i;
            else yield return instants[0];
            yield break;
        }
        yield return new DateTimeOffset(wall, zone.GetUtcOffset(wall)).ToUniversalTime();
    }

    /// <summary>The instant a skipped wall-clock time maps to: the first moment after the gap.</summary>
    internal static DateTimeOffset GapEnd(DateTime wall, TimeZoneInfo zone)
    {
        // The offset changes once near here, on a whole minute. Find the first minute with the new offset.
        var approx = DateTime.SpecifyKind(new DateTime(wall.Year, wall.Month, wall.Day, wall.Hour, wall.Minute, 0), DateTimeKind.Utc);
        var lo = approx.AddHours(-26); // before the change (old offset)
        var hi = approx.AddHours(26);  // after it (new offset)
        var before = zone.GetUtcOffset(lo);
        if (zone.GetUtcOffset(hi) == before) return new DateTimeOffset(wall, zone.GetUtcOffset(wall.AddHours(-3))).ToUniversalTime();
        while ((hi - lo).TotalMinutes > 1)
        {
            var mid = lo.AddMinutes(Math.Floor((hi - lo).TotalMinutes / 2));
            if (zone.GetUtcOffset(mid) == before) lo = mid; else hi = mid;
        }
        return new DateTimeOffset(hi);
    }

    /// <summary>The instant a one-off wall-clock time happens: a skipped time maps to the first moment after the gap, a repeated time to its first occurrence.</summary>
    public static DateTimeOffset InstantOf(DateTime wall, TimeZoneInfo zone)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall)) return GapEnd(wall, zone);
        if (zone.IsAmbiguousTime(wall)) return zone.GetAmbiguousTimeOffsets(wall).Select(o => new DateTimeOffset(wall, o).ToUniversalTime()).Min();
        return new DateTimeOffset(wall, zone.GetUtcOffset(wall)).ToUniversalTime();
    }

    public override string ToString() => Text;
}
