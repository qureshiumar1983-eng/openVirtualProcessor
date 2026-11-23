using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OpenVP.Core;

/// <summary>One change in a saved or exported trace: every field the debugger's trace has.</summary>
/// <param name="Value">A bool for digital signals, a number for analog, text for serial.</param>
public sealed record TraceRow(DateTimeOffset Time, string Signal, string Kind, object? Value, string Source,
    string? Symbol = null, string? Cause = null, int Depth = 0, long Chain = 0)
{
    public static TraceRow From(TraceEvent e) => new(e.Time, e.Signal, e.Kind.ToString(), Normalize(e.Kind.ToString(), e.Value), e.Source ?? "", e.Symbol, e.Cause, e.Depth, e.Chain);

    /// <summary>Value as it is stored: bool, long or string, whatever type the runtime used.</summary>
    public static object? Normalize(string kind, object? v)
    {
        if (kind.Equals("Digital", StringComparison.OrdinalIgnoreCase))
            return v switch { bool b => b, string s => s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase), null => false, _ => Convert.ToInt64(v, CultureInfo.InvariantCulture) != 0 };
        if (kind.Equals("Analog", StringComparison.OrdinalIgnoreCase))
            return v switch { bool b => b ? 1L : 0L, string s => long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0L, null => 0L, _ => Convert.ToInt64(v, CultureInfo.InvariantCulture) };
        return v?.ToString() ?? "";
    }

    /// <summary>The value as text for display and comparing: 1/0, a number, or the serial text.</summary>
    public string ValueText => Value switch { bool b => b ? "1" : "0", null => "", _ => Convert.ToString(Value, CultureInfo.InvariantCulture) ?? "" };
}

/// <summary>A trace and where it came from.</summary>
public sealed record TraceFile(int Slot, string Program, DateTimeOffset Saved, IReadOnlyList<TraceRow> Rows);

/// <summary>A trace file that can't be read.</summary>
public sealed class TraceFormatException(string message) : Exception(message);

/// <summary>Writes and reads traces as JSON and CSV. Both carry every field; reading either gives the same rows back.</summary>
public static class TraceIo
{
    public const string FormatName = "openvp-trace";
    public static readonly string[] CsvHeader = { "time", "signal", "kind", "value", "source", "symbol", "cause", "depth", "chain" };
    public const int MaxRows = 500_000;

    private static readonly JsonWriterOptions Wo = new() { Indented = false };

    // ---------------------------------------------------------------- JSON

    public static string ToJson(TraceFile f)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, Wo))
        {
            w.WriteStartObject();
            w.WriteString("format", FormatName);
            w.WriteNumber("version", 1);
            w.WriteNumber("slot", f.Slot);
            w.WriteString("program", f.Program);
            w.WriteString("saved", f.Saved);
            w.WriteNumber("count", f.Rows.Count);
            w.WriteStartArray("events");
            foreach (var r in f.Rows)
            {
                w.WriteStartObject();
                w.WriteString("time", r.Time);
                w.WriteString("signal", r.Signal);
                w.WriteString("kind", r.Kind);
                w.WritePropertyName("value");
                switch (r.Value)
                {
                    case bool b: w.WriteBooleanValue(b); break;
                    case string s: w.WriteStringValue(s); break;
                    case null: w.WriteNullValue(); break;
                    default: w.WriteNumberValue(Convert.ToInt64(r.Value, CultureInfo.InvariantCulture)); break;
                }
                w.WriteString("source", r.Source);
                if (r.Symbol != null) w.WriteString("symbol", r.Symbol); else w.WriteNull("symbol");
                if (r.Cause != null) w.WriteString("cause", r.Cause); else w.WriteNull("cause");
                w.WriteNumber("depth", r.Depth);
                w.WriteNumber("chain", r.Chain);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Read a trace JSON file: the OpenVP format, or a bare array of events.</summary>
    public static TraceFile FromJson(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 }); }
        catch (JsonException e) { throw new TraceFormatException("That isn't valid JSON: " + e.Message); }
        using (doc)
        {
            var root = doc.RootElement;
            JsonElement events;
            int slot = 0; var program = ""; var saved = DateTimeOffset.Now;
            if (root.ValueKind == JsonValueKind.Array) events = root;
            else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("events", out events) && events.ValueKind == JsonValueKind.Array)
            {
                if (root.TryGetProperty("format", out var fmt) && fmt.ValueKind == JsonValueKind.String && fmt.GetString() != FormatName)
                    throw new TraceFormatException("This JSON isn't an OpenVP trace.");
                if (root.TryGetProperty("slot", out var sl) && sl.TryGetInt32(out var sn)) slot = sn;
                if (root.TryGetProperty("program", out var pr) && pr.ValueKind == JsonValueKind.String) program = pr.GetString() ?? "";
                if (root.TryGetProperty("saved", out var sv) && sv.ValueKind == JsonValueKind.String && sv.TryGetDateTimeOffset(out var sd)) saved = sd;
            }
            else throw new TraceFormatException("This JSON isn't an OpenVP trace: it has no \"events\" list.");

            var rows = new List<TraceRow>();
            var n = 0;
            foreach (var e in events.EnumerateArray())
            {
                n++;
                if (rows.Count >= MaxRows) throw new TraceFormatException($"A trace can have at most {MaxRows:N0} rows.");
                if (e.ValueKind != JsonValueKind.Object) throw new TraceFormatException($"Event {n} isn't an object.");
                string Str(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                string? OptStr(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                long Num(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : 0;
                var signal = Str("signal");
                if (signal.Length == 0) throw new TraceFormatException($"Event {n} has no signal.");
                if (!e.TryGetProperty("time", out var tv) || tv.ValueKind != JsonValueKind.String || !tv.TryGetDateTimeOffset(out var time))
                    throw new TraceFormatException($"Event {n} (signal {signal}) has no valid time.");
                var kind = CanonKind(Str("kind")) ?? throw new TraceFormatException($"Event {n} (signal {signal}): kind must be Digital, Analog or Serial.");
                object? value = null;
                if (e.TryGetProperty("value", out var val))
                    value = val.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Number => val.TryGetInt64(out var l) ? l : (long)Math.Round(val.GetDouble()),
                        JsonValueKind.String => val.GetString(),
                        _ => null
                    };
                rows.Add(new TraceRow(time, signal, kind, TraceRow.Normalize(kind, value), Str("source"), OptStr("symbol"), OptStr("cause"), (int)Num("depth"), Num("chain")));
            }
            return new TraceFile(slot, program, saved, rows);
        }
    }

    private static string? CanonKind(string k) => k.ToLowerInvariant() switch { "digital" => "Digital", "analog" => "Analog", "serial" => "Serial", _ => null };

    // ---------------------------------------------------------------- CSV

    public static string ToCsv(IEnumerable<TraceRow> rows)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(',', CsvHeader)).Append("\r\n");
        foreach (var r in rows)
        {
            sb.Append(r.Time.ToString("O", CultureInfo.InvariantCulture)).Append(',')
              .Append(Q(r.Signal)).Append(',').Append(r.Kind).Append(',')
              .Append(Q(r.ValueText)).Append(',').Append(Q(r.Source)).Append(',')
              .Append(Q(r.Symbol ?? "")).Append(',').Append(Q(r.Cause ?? "")).Append(',')
              .Append(r.Depth.ToString(CultureInfo.InvariantCulture)).Append(',').Append(r.Chain.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        }
        return sb.ToString();
    }

    // Quote anything with a comma, quote or line break (and anything starting with a space, which spreadsheets trim).
    private static string Q(string s) =>
        s.Length > 0 && (s.AsSpan().IndexOfAny(",\"\r\n") >= 0 || s[0] == ' ' || s[^1] == ' ') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;

    public static TraceFile FromCsv(string csv)
    {
        var records = ParseCsv(csv);
        if (records.Count == 0) throw new TraceFormatException("The file is empty.");
        var head = records[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(string name) => head.IndexOf(name);
        var iTime = Col("time"); var iSig = Col("signal"); var iKind = Col("kind"); var iVal = Col("value");
        if (iTime < 0 || iSig < 0 || iKind < 0 || iVal < 0) throw new TraceFormatException("The first line must name the columns: time, signal, kind, value, source, symbol, cause, depth, chain.");
        int iSrc = Col("source"), iSym = Col("symbol"), iCause = Col("cause"), iDepth = Col("depth"), iChain = Col("chain");
        var rows = new List<TraceRow>();
        for (var i = 1; i < records.Count; i++)
        {
            var c = records[i];
            if (c.Count == 1 && c[0].Length == 0) continue; // blank line
            if (rows.Count >= MaxRows) throw new TraceFormatException($"A trace can have at most {MaxRows:N0} rows.");
            string At(int k) => k >= 0 && k < c.Count ? c[k] : "";
            if (!DateTimeOffset.TryParse(At(iTime), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time))
                throw new TraceFormatException($"Line {i + 1}: '{At(iTime)}' isn't a time.");
            var kind = CanonKind(At(iKind)) ?? throw new TraceFormatException($"Line {i + 1}: kind must be Digital, Analog or Serial.");
            if (At(iSig).Length == 0) throw new TraceFormatException($"Line {i + 1}: no signal.");
            var text = At(iVal);
            object? value = kind switch
            {
                "Digital" => text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase),
                "Analog" => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0L,
                _ => text
            };
            var sym = At(iSym); var cause = At(iCause);
            _ = int.TryParse(At(iDepth), NumberStyles.Integer, CultureInfo.InvariantCulture, out var depth);
            _ = long.TryParse(At(iChain), NumberStyles.Integer, CultureInfo.InvariantCulture, out var chain);
            rows.Add(new TraceRow(time, At(iSig), kind, value, At(iSrc), sym.Length > 0 ? sym : null, cause.Length > 0 ? cause : null, depth, chain));
        }
        return new TraceFile(0, "", DateTimeOffset.Now, rows);
    }

    private static List<List<string>> ParseCsv(string s)
    {
        var all = new List<List<string>>();
        var row = new List<string>();
        var sb = new StringBuilder();
        var inQ = false; var any = false;
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (inQ)
            {
                if (ch == '"') { if (i + 1 < s.Length && s[i + 1] == '"') { sb.Append('"'); i++; } else inQ = false; }
                else sb.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"': inQ = true; any = true; break;
                case ',': row.Add(sb.ToString()); sb.Clear(); any = true; break;
                case '\r': break;
                case '\n': row.Add(sb.ToString()); sb.Clear(); all.Add(row); row = new(); any = false; break;
                default: sb.Append(ch); any = true; break;
            }
        }
        if (inQ) throw new TraceFormatException("A quoted value is missing its closing quote.");
        if (any || sb.Length > 0 || row.Count > 0) { row.Add(sb.ToString()); all.Add(row); }
        return all;
    }

    /// <summary>Read a trace in either format: JSON if it starts with { or [, else CSV.</summary>
    public static TraceFile Parse(string text)
    {
        var t = text.TrimStart('﻿', ' ', '\r', '\n', '\t');
        return t.StartsWith('{') || t.StartsWith('[') ? FromJson(t) : FromCsv(t);
    }
}
