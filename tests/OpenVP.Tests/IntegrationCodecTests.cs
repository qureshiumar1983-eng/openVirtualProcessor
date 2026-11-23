using System.Text;
using System.Text.RegularExpressions;
using OpenVP.Core;
using OpenVP.Core.Integrations;
using OpenVP.Simpl;

namespace OpenVP.Tests;

/// <summary>OSC and Art-Net packets against the bytes in the public specifications, value parsing, topic names, and the Prometheus text format.</summary>
public static class IntegrationCodecTests
{
    private static byte[] Hex(string s) => Convert.FromHexString(s.Replace(" ", ""));

    // ---------------------------------------------------------------- OSC (opensoundcontrol.stanford.edu/spec-1_0.html)

    [Test]
    public static void Osc_matches_the_byte_examples_of_the_specification()
    {
        // "/oscillator/4/frequency" ,f 440.0 : the spec's example, 32 bytes
        var a = OscCodec.Encode(new OscMessage("/oscillator/4/frequency", [440.0f]));
        Assert.Equal("2f6f7363696c6c61746f722f342f6672657175656e6379002c660000" + "43dc0000", Convert.ToHexString(a).ToLowerInvariant(), "oscillator example");
        // "/foo" ,iisff 1000 -1 "hello" 1.234 5.678 : the spec's second example
        var b = OscCodec.Encode(new OscMessage("/foo", [1000, -1, "hello", 1.234f, 5.678f]));
        Assert.Equal("2f666f6f00000000" + "2c696973666600" + "00" + "000003e8" + "ffffffff" + "68656c6c6f000000" + "3f9df3b6" + "40b5b22d", Convert.ToHexString(b).ToLowerInvariant(), "foo example");
        Assert.Equal(0, b.Length % 4, "every part is padded to four bytes");

        Assert.True(OscCodec.TryDecodeMessage(b, out var m), "decodes");
        Assert.Equal("/foo", m!.Address, "address");
        Assert.Equal(1000, (int)m.Args[0]!, "int"); Assert.Equal(-1, (int)m.Args[1]!, "negative int"); Assert.Equal("hello", (string)m.Args[2]!, "string");
        Assert.True(Math.Abs((float)m.Args[3]! - 1.234f) < 1e-6, "float"); Assert.True(Math.Abs((float)m.Args[4]! - 5.678f) < 1e-6, "float 2");
    }

    [Test]
    public static void Osc_round_trips_every_argument_type_and_reads_bundles()
    {
        var msg = new OscMessage("/x/y", [42, 5_000_000_000L, 0.25f, 0.5, "héllo", true, false, null, new byte[] { 1, 2, 3 }]);
        var bytes = OscCodec.Encode(msg);
        Assert.True(OscCodec.TryDecodeMessage(bytes, out var d), "decodes");
        Assert.Equal(9, d!.Args.Count, "argument count");
        Assert.Equal(5_000_000_000L, (long)d.Args[1]!, "int64"); Assert.Equal(0.5, (double)d.Args[3]!, "double"); Assert.Equal("héllo", (string)d.Args[4]!, "utf-8 string");
        Assert.True((bool)d.Args[5]! && !(bool)d.Args[6]!, "T and F carry no bytes"); Assert.True(d.Args[7] == null, "nil");
        Assert.Equal("010203", Convert.ToHexString((byte[])d.Args[8]!).ToLowerInvariant(), "blob");

        // A bundle: "#bundle\0", time tag, then size-prefixed elements.
        var e1 = OscCodec.Encode(new OscMessage("/a", [1])); var e2 = OscCodec.Encode(new OscMessage("/b", ["z"]));
        var bundle = new List<byte>(); bundle.AddRange(Encoding.ASCII.GetBytes("#bundle\0")); bundle.AddRange(new byte[7]); bundle.Add(1);
        foreach (var e in new[] { e1, e2 }) { bundle.AddRange(new byte[] { 0, 0, 0, (byte)e.Length }); bundle.AddRange(e); }
        var msgs = OscCodec.DecodePacket(bundle.ToArray());
        Assert.Equal("/a,/b", string.Join(",", msgs.Select(x => x.Address)), "both messages of the bundle");

        // Message with no type tag string (older senders) has no arguments.
        Assert.True(OscCodec.TryDecodeMessage(Hex("2f666f6f00000000"), out var bare) && bare!.Args.Count == 0, "no type tags");
    }

    [Test]
    public static void Osc_ignores_damaged_packets()
    {
        var good = OscCodec.Encode(new OscMessage("/foo", [1000, "hello"]));
        for (var cut = 0; cut < good.Length; cut++)
        {
            // Must never throw, whatever the cut.
            OscCodec.DecodePacket(good.AsSpan(0, cut));
        }
        Assert.True(!OscCodec.TryDecodeMessage(Hex("6e6f2d736c617368000000002c690000"), out _), "an address must start with /");
        Assert.True(!OscCodec.TryDecodeMessage(Hex("2f6600002c7a0000"), out _), "unknown type tag can't be sized");
        Assert.True(!OscCodec.TryDecodeMessage(Hex("2f6600002c690000000000"), out _), "missing argument bytes");
        Assert.Equal(0, OscCodec.DecodePacket(Hex("2362756e646c6500")).Count, "a truncated bundle gives nothing");
        var rnd = new Random(7);
        for (var i = 0; i < 2000; i++) { var junk = new byte[rnd.Next(0, 80)]; rnd.NextBytes(junk); OscCodec.DecodePacket(junk); }
        Assert.Throws<ArgumentException>(() => OscCodec.Encode(new OscMessage("/x", [DateTime.Now])));
    }

    // ---------------------------------------------------------------- Art-Net 4 (ArtDMX)

    [Test]
    public static void ArtNet_dmx_packet_matches_the_specification_layout()
    {
        // universe 1, sequence 5, data 255 0 128 : padded to 4 bytes (the length must be even)
        var p = ArtNetCodec.EncodeDmx(1, 5, new byte[] { 255, 0, 128 });
        Assert.Equal("4172742d4e657400" + "0050" + "000e" + "05" + "00" + "01" + "00" + "0004" + "ff008000", Convert.ToHexString(p).ToLowerInvariant(), "ArtDMX bytes");
        Assert.Equal(22, p.Length, "18 header bytes + 4 data");

        // Universe 0x1234 is Net 0x12, SubUni 0x34; the OpCode is sent low byte first (0x5000), the length high byte first.
        var q = ArtNetCodec.EncodeDmx(0x1234, 255, new byte[512]);
        Assert.Equal(0x34, q[14], "SubUni"); Assert.Equal(0x12, q[15], "Net"); Assert.Equal(0x00, q[8], "opcode low"); Assert.Equal(0x50, q[9], "opcode high");
        Assert.Equal(2, q[16], "length high (512 = 0x0200)"); Assert.Equal(0, q[17], "length low"); Assert.Equal(530, q.Length, "full universe");
        Assert.Equal(14, q[11], "protocol version 14");

        Assert.True(ArtNetCodec.TryDecodeDmx(p, out var d), "decodes");
        Assert.Equal(1, d!.Universe, "universe"); Assert.Equal(5, d.Sequence, "sequence"); Assert.Equal("ff008000", Convert.ToHexString(d.Data).ToLowerInvariant(), "data");
        Assert.True(ArtNetCodec.TryDecodeDmx(q, out var d2) && d2!.Universe == 0x1234 && d2.Data.Length == 512, "high universe");
    }

    [Test]
    public static void ArtNet_rejects_other_packets_and_scales_values()
    {
        var p = ArtNetCodec.EncodeDmx(0, 1, new byte[] { 1, 2 });
        var wrongId = (byte[])p.Clone(); wrongId[0] = (byte)'X';
        var wrongOp = (byte[])p.Clone(); wrongOp[9] = 0x20;           // ArtPoll is 0x2000
        var oldVer = (byte[])p.Clone(); oldVer[11] = 13;
        var longLen = (byte[])p.Clone(); longLen[17] = 200;           // claims more data than there is
        foreach (var bad in new[] { wrongId, wrongOp, oldVer, longLen, p[..10], Array.Empty<byte>() })
            Assert.True(!ArtNetCodec.TryDecodeDmx(bad, out _), "rejected");
        Assert.Throws<ArgumentOutOfRangeException>(() => ArtNetCodec.EncodeDmx(32768, 0, new byte[2]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ArtNetCodec.EncodeDmx(0, 0, new byte[513]));

        Assert.Equal(0, (int)ArtNetCodec.ToByte(0), "0"); Assert.Equal(255, (int)ArtNetCodec.ToByte(65535), "full scale");
        Assert.Equal(128, (int)ArtNetCodec.ToByte(32768), "half"); Assert.Equal(255, (int)ArtNetCodec.ToByte(70000), "clamped");
        Assert.Equal(65535, (int)ArtNetCodec.FromByte(255), "255 is full scale"); Assert.Equal(257, (int)ArtNetCodec.FromByte(1), "1 -> 257");
        for (var b = 0; b < 256; b++) Assert.Equal(b, (int)ArtNetCodec.ToByte(ArtNetCodec.FromByte((byte)b)), "byte survives a round trip");
    }

    // ---------------------------------------------------------------- values and names

    [Test]
    public static void Values_parse_by_kind_and_names_become_safe_segments()
    {
        object v; string? err;
        foreach (var t in new[] { "1", "true", "TRUE", "on", " 1 " }) Assert.True(SignalValues.TryParse(SigKind.Digital, t, out v, out _) && (bool)v, $"'{t}' is high");
        foreach (var t in new[] { "0", "false", "off" }) Assert.True(SignalValues.TryParse(SigKind.Digital, t, out v, out _) && !(bool)v, $"'{t}' is low");
        Assert.True(!SignalValues.TryParse(SigKind.Digital, "maybe", out _, out err) && err != null, "junk is refused with a reason");
        Assert.True(SignalValues.IsPulse(" Pulse "), "pulse word");
        Assert.True(SignalValues.TryParse(SigKind.Analog, "50%", out v, out _) && (ushort)v is 32767 or 32768, "50% is half scale");
        Assert.True(SignalValues.TryParse(SigKind.Analog, "100%", out v, out _) && (ushort)v == 65535, "100%");
        Assert.True(SignalValues.TryParse(SigKind.Analog, "1234", out v, out _) && (ushort)v == 1234, "plain number");
        Assert.True(SignalValues.TryParse(SigKind.Analog, "99999", out v, out _) && (ushort)v == 65535, "clamped high");
        Assert.True(SignalValues.TryParse(SigKind.Analog, "-5", out v, out _) && (ushort)v == 0, "clamped low");
        Assert.True(!SignalValues.TryParse(SigKind.Analog, "abc", out _, out _) && !SignalValues.TryParse(SigKind.Analog, "NaN", out _, out _), "not a number");
        Assert.True(SignalValues.TryParse(SigKind.Serial, "any text, even 1", out v, out _) && (string)v == "any text, even 1", "serial is text");
        Assert.Equal("1,0,42,hi", string.Join(",", new object[] { true, false, (ushort)42, "hi" }.Select(SignalValues.Format)), "formatting");

        Assert.Equal("Room_On", SignalValues.Segment("Room_On"), "plain");
        Assert.Equal("Vol_Lvl__1__", SignalValues.Segment("Vol Lvl [1]#"), "spaces, brackets and # become _");
        Assert.Equal("a_b_c", SignalValues.Segment("a/b+c"), "separators and wildcards go");
        var segs = SignalValues.Segments(new[] { "A B", "A_B", "A/B", "A B" });
        Assert.Equal("A_B", segs["A B"], "first keeps its name"); Assert.Equal("A_B_2", segs["A_B"], "a clash gets _2"); Assert.Equal("A_B_3", segs["A/B"], "and _3");
        Assert.Equal(3, segs.Count, "a repeated name is one entry");
    }

    // ---------------------------------------------------------------- Prometheus text format

    private static readonly Regex Sample = new(@"^(?<name>[a-zA-Z_:][a-zA-Z0-9_:]*)(\{(?<labels>(?:[a-zA-Z_][a-zA-Z0-9_]*=""(?:[^""\\\n]|\\.)*"",?)*)\})? (?<value>[-+]?(?:\d+(?:\.\d+)?(?:[eE][-+]?\d+)?|Inf|NaN))$", RegexOptions.Compiled);

    /// <summary>Parses the text format strictly: HELP and TYPE before the samples of a family, once each, no repeated series.</summary>
    internal static List<(string Name, Dictionary<string, string> Labels, double Value)> ParsePrometheus(string text)
    {
        Assert.True(text.EndsWith('\n'), "ends with a newline");
        var helped = new HashSet<string>(); var typed = new Dictionary<string, string>(); var seen = new HashSet<string>(); var list = new List<(string, Dictionary<string, string>, double)>();
        foreach (var line in text.TrimEnd('\n').Split('\n'))
        {
            if (line.StartsWith("# HELP ")) { var n = line.Split(' ')[2]; Assert.True(helped.Add(n), $"HELP for {n} once"); continue; }
            if (line.StartsWith("# TYPE "))
            {
                var parts = line.Split(' '); Assert.True(parts[3] is "counter" or "gauge" or "histogram" or "summary" or "untyped", $"valid type for {parts[2]}");
                Assert.True(typed.TryAdd(parts[2], parts[3]), $"TYPE for {parts[2]} once"); continue;
            }
            Assert.True(!line.StartsWith('#') && line.Length > 0, "no stray comment or blank lines: " + line);
            var m = Sample.Match(line);
            Assert.True(m.Success, "valid sample line: " + line);
            var name = m.Groups["name"].Value;
            Assert.True(typed.ContainsKey(name) && helped.Contains(name), $"{name} has HELP and TYPE before its samples");
            if (typed[name] == "counter") Assert.True(name.EndsWith("_total"), $"counter {name} ends in _total");
            var labels = new Dictionary<string, string>();
            foreach (Match lm in Regex.Matches(m.Groups["labels"].Value, @"([a-zA-Z_][a-zA-Z0-9_]*)=""((?:[^""\\]|\\.)*)"""))
                labels[lm.Groups[1].Value] = lm.Groups[2].Value.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
            Assert.True(seen.Add(name + "|" + string.Join(",", labels.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value))), "a series appears once: " + line);
            var raw = m.Groups["value"].Value;
            list.Add((name, labels, raw == "NaN" ? double.NaN : raw.EndsWith("Inf") ? (raw[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity) : double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture)));
        }
        return list;
    }

    [Test]
    public static void Prometheus_writer_escapes_labels_and_formats_numbers()
    {
        var w = new PrometheusWriter();
        w.Family("t_info", "gauge", "Multi\nline help").Sample("t_info", 1, ("a", "quote\" slash\\ nl\n"), ("b", "plain"));
        w.Single("t_val", "gauge", "v", 1234567.5).Single("t_nan", "gauge", "n", double.NaN).Single("t_inf", "gauge", "n", double.PositiveInfinity);
        var text = w.ToString();
        Assert.True(text.Contains("t_info{a=\"quote\\\" slash\\\\ nl\\n\",b=\"plain\"} 1\n"), "label escapes: " + text);
        Assert.True(text.Contains("# HELP t_info Multi\\nline help\n"), "help newline is escaped");
        var parsed = ParsePrometheus(text);
        Assert.Equal("quote\" slash\\ nl\n", parsed.First(s => s.Name == "t_info").Labels["a"], "label value comes back");
        Assert.True(text.Contains("t_val 1234567.5\n") && text.Contains("t_nan NaN\n") && text.Contains("t_inf +Inf\n"), "numbers");
        Assert.Throws<InvalidOperationException>(() => w.Family("t_val", "gauge", "again"));
    }

    [Test]
    public static void Prometheus_output_of_a_real_processor_is_valid_and_complete()
    {
        using var p = new Processor(Paths.TempData(), 3);
        p.AddLoader(new SimplLoader());
        p.LoadProgram(1, Path.Combine(Paths.RepoRoot, "samples", "SimplWindows", "SampleProgram.smw"));
        p.Slot(1).ForceSignal("toggle_press", true);
        using var host = new IntegrationsHost(p, () => throw new InvalidOperationException("no MQTT in this test"));
        var text = PrometheusText.Render(p, host, s => (s, s * 2));
        var samples = ParsePrometheus(text);
        double One(string name, string? slot = null) => samples.Single(s => s.Name == name && (slot == null || s.Labels.GetValueOrDefault("slot") == slot)).Value;

        foreach (var n in new[] { "openvp_info", "openvp_uptime_seconds", "openvp_process_cpu_percent", "openvp_process_resident_memory_bytes", "openvp_process_threads",
                     "openvp_threadpool_threads", "openvp_devices_online", "openvp_panel_connections", "openvp_alerts", "openvp_mqtt_connected", "openvp_webhook_deliveries_total",
                     "openvp_osc_messages_received_total", "openvp_artnet_packets_sent_total" })
            Assert.True(samples.Any(s => s.Name == n), $"{n} is there");
        foreach (var n in new[] { "openvp_slot_running", "openvp_slot_events_per_second", "openvp_slot_events_total", "openvp_slot_busy_percent", "openvp_slot_queue_depth", "openvp_slot_devices",
                     "openvp_slot_devices_online", "openvp_slot_panel_connections", "openvp_slot_crashes_total", "openvp_slot_restarts_total", "openvp_slot_alerts" })
            Assert.Equal(3, samples.Count(s => s.Name == n), $"{n} has one series per slot");
        Assert.Equal(1.0, One("openvp_slot_running", "1"), "slot 1 runs"); Assert.Equal(0.0, One("openvp_slot_running", "2"), "slot 2 is empty");
        Assert.Equal(5, samples.Count(s => s.Name == "openvp_slot_state" && s.Labels["slot"] == "1"), "a series per state");
        Assert.Equal(1.0, samples.Single(s => s.Name == "openvp_slot_state" && s.Labels["slot"] == "1" && s.Labels["state"] == "running").Value, "state=running is 1");
        Assert.Equal(1.0, samples.Single(s => s.Name == "openvp_slot_state" && s.Labels["slot"] == "2" && s.Labels["state"] == "empty").Value, "state=empty is 1 for slot 2");
        Assert.Equal(2.0, One("openvp_slot_crashes_total", "2"), "crash totals come from the supervisor"); Assert.Equal(6.0, One("openvp_slot_restarts_total", "3"), "restart totals");
        Assert.True(One("openvp_slot_devices", "1") >= 1, "the sample program registers panels");
        Assert.Equal("SampleProgram", samples.Single(s => s.Name == "openvp_slot_info" && s.Labels["slot"] == "1").Labels["program"], "program name label");
        Assert.True(!text.Contains("\r"), "Unix line endings");
    }
}
