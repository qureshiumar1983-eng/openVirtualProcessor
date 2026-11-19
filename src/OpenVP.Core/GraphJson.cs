using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
namespace OpenVP.Core;

/// <summary>
/// The program graph (docs/debugger-paths.md section 1) and the signal list as JSON bytes, written straight to a
/// buffer, plus the per-program cache that keeps the graph's bytes so a repeat request costs nothing.
/// </summary>
public static class GraphJson
{
    /// <summary>
    /// The graph in the shape of docs/debugger-paths.md. With <paramref name="compact"/>, pins that are empty
    /// ("" = unwired) or a constant ("0", "1") are left out of each symbol's inputs/outputs (the rest keep their
    /// order, so a pin's position no longer says its pin number) and a symbol left with no pins is omitted.
    /// </summary>
    public static byte[] Write(ProgramGraph g, bool compact)
    {
        var buf = new System.Buffers.ArrayBufferWriter<byte>(compact ? 64 * 1024 : 1024 * 1024);
        using (var w = new Utf8JsonWriter(buf))
        {
            w.WriteStartObject();
            w.WriteString("kind", g.Kind);
            w.WriteStartArray("symbols");
            foreach (var s in g.Symbols)
            {
                if (compact && !HasRealPin(s.Inputs) && !HasRealPin(s.Outputs)) continue;
                w.WriteStartObject();
                w.WriteString("id", s.Id);
                w.WriteString("name", s.Name);
                w.WriteString("path", s.Path);
                w.WriteString("group", s.Group);
                w.WriteBoolean("device", s.Device);
                WritePins(w, "inputs", s.Inputs, compact);
                WritePins(w, "outputs", s.Outputs, compact);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("signals");
            foreach (var s in g.Signals)
            {
                w.WriteStartObject();
                w.WriteString("name", s.Name);
                w.WriteString("kind", s.Kind);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return buf.WrittenSpan.ToArray();
    }

    private static bool IsReal(string pin) => pin.Length > 0 && pin != "0" && pin != "1";

    private static bool HasRealPin(IReadOnlyList<string> pins)
    {
        for (int i = 0; i < pins.Count; i++) if (IsReal(pins[i])) return true;
        return false;
    }

    private static void WritePins(Utf8JsonWriter w, string name, IReadOnlyList<string> pins, bool compact)
    {
        w.WriteStartArray(name);
        for (int i = 0; i < pins.Count; i++)
            if (!compact || IsReal(pins[i])) w.WriteStringValue(pins[i]);
        w.WriteEndArray();
    }

    /// <summary>The slot's signals as [{"name","kind","value"}], the shape GET /signals has always had.</summary>
    public static byte[] WriteSignals(IReadOnlyList<SignalInfo> signals)
    {
        var buf = new System.Buffers.ArrayBufferWriter<byte>(Math.Max(256, signals.Count * 64));
        using (var w = new Utf8JsonWriter(buf))
        {
            w.WriteStartArray();
            foreach (var s in signals)
            {
                w.WriteStartObject();
                w.WriteString("name", s.Name);
                w.WriteString("kind", s.Kind.ToString());
                w.WritePropertyName("value");
                switch (s.Value)
                {
                    case null: w.WriteNullValue(); break;
                    case bool b: w.WriteBooleanValue(b); break;
                    case string str: w.WriteStringValue(str); break;
                    case int i: w.WriteNumberValue(i); break;
                    case uint u: w.WriteNumberValue(u); break;
                    case ushort us: w.WriteNumberValue(us); break;
                    case short sh: w.WriteNumberValue(sh); break;
                    case long l: w.WriteNumberValue(l); break;
                    case ulong ul: w.WriteNumberValue(ul); break;
                    case byte by: w.WriteNumberValue(by); break;
                    default: JsonSerializer.Serialize(w, s.Value, s.Value.GetType()); break;
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
        }
        return buf.WrittenSpan.ToArray();
    }

    /// <summary>One serialized form of the graph with its strong ETag.</summary>
    public sealed class Form(byte[] bytes)
    {
        public byte[] Bytes { get; } = bytes;
        public string ETag { get; } = "\"" + Convert.ToHexString(SHA256.HashData(bytes), 0, 12).ToLowerInvariant() + "\"";
    }

    /// <summary>The graph of one loaded program, serialized lazily and kept as long as that program object lives.</summary>
    public sealed class Entry(IProgramGraphSource source)
    {
        private readonly object _gate = new();
        private ProgramGraph? _graph;
        private Form? _full, _compact;

        public Form Get(bool compact)
        {
            lock (_gate)
            {
                // The wiring of a loaded program never changes (both runtimes build it from the parsed file, which is
                // read-only after load), so the first answer is the answer until the program is replaced.
                _graph ??= source.GetGraph();
                return compact ? _compact ??= new Form(Write(_graph, true)) : _full ??= new Form(Write(_graph, false));
            }
        }
    }

    // Keyed by the runtime object: loading, unloading or replacing a program makes a new (or no) runtime, so the
    // entry goes with the old one and nothing needs invalidating by hand.
    private static readonly ConditionalWeakTable<IProgramGraphSource, Entry> Cache = new();

    public static Form For(IProgramGraphSource runtime, bool compact) =>
        Cache.GetValue(runtime, r => new Entry(r)).Get(compact);

    /// <summary>True if an If-None-Match header value (a list of ETags, weak or strong, or "*") names this ETag.</summary>
    public static bool NotModified(string? ifNoneMatch, string etag)
    {
        if (string.IsNullOrEmpty(ifNoneMatch)) return false;
        foreach (var part in ifNoneMatch.Split(','))
        {
            var t = part.Trim();
            if (t == "*") return true;
            if (t.StartsWith("W/", StringComparison.Ordinal)) t = t[2..];
            if (t == etag) return true;
        }
        return false;
    }
}
