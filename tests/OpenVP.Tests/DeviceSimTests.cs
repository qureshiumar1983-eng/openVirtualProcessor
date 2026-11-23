using OpenVP.Core;
using OpenVP.Lpz;

namespace OpenVP.Tests;

/// <summary>A clock the test winds by hand.</summary>
public sealed class ManualClock : ISimClock
{
    private readonly List<(long Due, long Every, Action Run, int Id)> _items = new();
    private long _now; private int _next;
    public IDisposable After(int ms, Action a) { var id = ++_next; lock (_items) _items.Add((_now + ms, 0, a, id)); return new H(this, id); }
    public IDisposable Every(int ms, Action a) { var id = ++_next; lock (_items) _items.Add((_now + ms, ms, a, id)); return new H(this, id); }
    public int Pending { get { lock (_items) return _items.Count; } }
    public void Advance(int ms)
    {
        var end = _now + ms;
        while (true)
        {
            (long Due, long Every, Action Run, int Id) next;
            lock (_items)
            {
                var due = _items.Where(i => i.Due <= end).OrderBy(i => i.Due).ToList();
                if (due.Count == 0) break;
                next = due[0];
                _items.RemoveAll(i => i.Id == next.Id);
                _now = Math.Max(_now, next.Due);
                if (next.Every > 0) _items.Add((next.Due + next.Every, next.Every, next.Run, next.Id));
            }
            next.Run();
        }
        _now = end;
    }
    private sealed class H(ManualClock c, int id) : IDisposable { public void Dispose() { lock (c._items) c._items.RemoveAll(i => i.Id == id); } }
}

/// <summary>The reply engine of the device simulators, their generic profiles, and a simulator answering a COM port of a real program.</summary>
public static class DeviceSimTests
{
    private static (SimEngine engine, List<string> sent, ManualClock clock) Make(SimDef def)
    {
        var sent = new List<string>();
        var clock = new ManualClock();
        var e = new SimEngine(def, (_, t) => { lock (sent) sent.Add(t); }, clock);
        return (e, sent, clock);
    }

    private static SimRule Rule(string match, string reply, int delay = 0, bool regex = false, params (string, string)[] set) =>
        new() { Match = match, Reply = reply, DelayMs = delay, Regex = regex, Set = set.ToDictionary(s => s.Item1, s => s.Item2) };

    [Test]
    public static void Literal_rule_replies_after_its_delay_and_sets_state()
    {
        var def = new SimDef { State = { ["power"] = "0" } };
        def.Rules.Add(Rule("PWR ON\\r", "PWR=1\\r", 50, false, ("power", "1")));
        var (e, sent, clock) = Make(def);
        e.Receive(0, "PWR ON\r");
        Assert.Equal("1", e.Get("power")!, "state changes at once");
        Assert.Equal(0, sent.Count, "reply waits");
        clock.Advance(49);
        Assert.Equal(0, sent.Count, "not yet at 49 ms");
        clock.Advance(1);
        Assert.Equal("PWR=1\r", string.Concat(sent), "reply at 50 ms");
    }

    [Test]
    public static void Regex_captures_and_state_placeholders_fill_the_reply()
    {
        var def = new SimDef { State = { ["vol"] = "10", ["lamp"] = "42" } };
        def.Rules.Add(Rule(@"VOL (\d+)\r", "VOL={1} (was {vol})\\r", 0, true, ("vol", "{1}")));
        def.Rules.Add(Rule("LAMP?\\r", "LAMP={lamp:D5}\\r"));
        var (e, sent, _) = Make(def);
        e.Receive(0, "VOL 55\r");
        Assert.Equal("VOL=55 (was 55)\r", sent[0], "state is set before the reply is built");
        e.Receive(0, "LAMP?\r");
        Assert.Equal("LAMP=00042\r", sent[1], "number format");
    }

    [Test]
    public static void State_values_can_be_worked_out()
    {
        var def = new SimDef { State = { ["vol"] = "50" } };
        def.Rules.Add(Rule("UP\\r", "{vol}\\r", 0, false, ("vol", "{vol}+5")));
        def.Rules.Add(Rule("DOWN\\r", "{vol}\\r", 0, false, ("vol", "{vol}-20")));
        var (e, sent, _) = Make(def);
        e.Receive(0, "UP\r"); e.Receive(0, "UP\r"); e.Receive(0, "DOWN\r");
        Assert.Equal("55|60|40", string.Join("|", sent.Select(s => s.Trim())), "55, 60, 40");
    }

    [Test]
    public static void Commands_split_across_chunks_or_run_together_both_work()
    {
        var def = new SimDef();
        def.Rules.Add(Rule("PWR ON\\r", "A\\r"));
        def.Rules.Add(Rule("PWR OFF\\r", "B\\r"));
        var (e, sent, _) = Make(def);
        e.Receive(0, "PWR O"); Assert.Equal(0, sent.Count, "half a command");
        e.Receive(0, "N\rPWR OFF\r");
        Assert.Equal("A\r|B\r", string.Join("|", sent), "split command, then two in one chunk, in order");
    }

    [Test]
    public static void Matching_can_ignore_case_and_unknown_text_is_kept_for_later()
    {
        var def = new SimDef();
        def.Rules.Add(new SimRule { Match = "hello\\r", Reply = "HI\\r", IgnoreCase = true });
        var (e, sent, _) = Make(def);
        e.Receive(0, "xx");
        Assert.Equal(1L, e.Unmatched, "counted as unmatched");
        e.Receive(0, "HeLLo\r");
        Assert.Equal("HI\r", string.Concat(sent), "case-insensitive");
    }

    [Test]
    public static void Timed_messages_and_connect_messages_are_sent()
    {
        var def = new SimDef { State = { ["temp"] = "20" } };
        def.OnConnect.Add("READY\\r");
        def.Unsolicited.Add(new SimTimer { EveryMs = 1000, Text = "TEMP={temp}\\r" });
        var (e, sent, clock) = Make(def);
        e.Connected(0);
        Assert.Equal("READY\r", string.Concat(sent), "connect message");
        e.Start();
        clock.Advance(2500);
        e.SetState("temp", "25");
        clock.Advance(1000);
        Assert.Equal("READY\r|TEMP=20\r|TEMP=20\r|TEMP=25\r", string.Join("|", sent), "every second, with current state");
        e.Stop();
        clock.Advance(5000);
        Assert.Equal(4, sent.Count, "stopped");
    }

    [Test]
    public static void Validation_catches_bad_regex_and_values()
    {
        Assert.Throws<ArgumentException>(() => SimEngine.Validate(new SimDef { Rules = { new SimRule { Match = "(", Regex = true } } }));
        Assert.Throws<ArgumentException>(() => SimEngine.Validate(new SimDef { Rules = { new SimRule { Match = "" } } }));
        Assert.Throws<ArgumentException>(() => SimEngine.Validate(new SimDef { Kind = "serial" }));
        Assert.Throws<ArgumentException>(() => SimEngine.Validate(new SimDef { Unsolicited = { new SimTimer { EveryMs = 5, Text = "x" } } }));
        SimEngine.Validate(new SimDef { Rules = { new SimRule { Match = "a+", Regex = true } } });
    }

    [Test]
    public static void Escapes_round_trip()
    {
        Assert.Equal("PWR ON\r\n\t\0\\x", SimText.Unescape("PWR ON\\r\\n\\t\\0\\\\x"), "unescape");
        Assert.Equal("\r\u0001A", SimText.Unescape("\\x0D\\x01A"), "hex escapes");
        Assert.Equal("a\\r\\x01", SimText.Escape("a\r\u0001"), "escape");
        Assert.Equal("50 57 52 0D", SimText.Hex("PWR\r"), "hex view");
    }

    [Test]
    public static void Every_generic_profile_is_valid_and_answers()
    {
        foreach (var p in SimProfiles.All) SimEngine.Validate(p.Create());
        SimDef Def(string id) => SimProfiles.Create(id)!;

        var (pj, s1, c1) = Make(Def("projector"));
        pj.Receive(0, "PWR?\r"); c1.Advance(100); pj.Receive(0, "PWR ON\r"); c1.Advance(100); pj.Receive(0, "PWR?\r"); c1.Advance(100); pj.Receive(0, "INPUT HDMI2\r"); c1.Advance(100); pj.Receive(0, "INPUT?\r"); c1.Advance(100); pj.Receive(0, "LAMP?\r"); c1.Advance(100); pj.Receive(0, "BOGUS\r"); c1.Advance(100);
        Assert.Equal("PWR=0\r|PWR=1\r|PWR=1\r|INPUT=HDMI2\r|INPUT=HDMI2\r|LAMP=1234\r|ERR\r", string.Join("|", s1), "projector");

        var (dsp, s2, c2) = Make(Def("dsp"));
        dsp.Receive(0, "SET Mic1 VOL 70\r"); c2.Advance(100); dsp.Receive(0, "GET Mic1 VOL\r"); c2.Advance(100); dsp.Receive(0, "SET Aux MUTE 1\r"); c2.Advance(100); dsp.Receive(0, "GET Aux MUTE\r"); c2.Advance(100); dsp.Receive(0, "UP Program\r"); c2.Advance(100);
        Assert.Equal("Mic1 VOL 70\r|Mic1 VOL 70\r|Aux MUTE 1\r|Aux MUTE 1\r|Program VOL 55\r", string.Join("|", s2), "dsp named levels");
        Assert.Equal("70", dsp.Get("Mic1.vol")!, "state per named level");
        Assert.Equal("40", dsp.Get("Mic2.vol")!, "others untouched");

        var (sw, s3, c3) = Make(Def("switcher"));
        sw.Receive(0, "ROUTE 2 3\r"); c3.Advance(100); sw.Receive(0, "ROUTE? 3\r"); c3.Advance(100); sw.Receive(0, "STATUS\r"); c3.Advance(100);
        Assert.Equal("ROUTE 2 3 OK\r|OUT3=2\r|OUT1=1 OUT2=2 OUT3=2 OUT4=4\r", string.Join("|", s3), "switcher");

        var (dp, s4, c4) = Make(Def("display"));
        dp.Receive(0, "POWER ON\r"); c4.Advance(100); dp.Receive(0, "VOL 33\r"); c4.Advance(100); dp.Receive(0, "MUTE ON\r"); c4.Advance(100); dp.Receive(0, "VOL?\r"); c4.Advance(100);
        Assert.Equal("POWER=ON\r|VOL=33\r|MUTE=ON\r|VOL=33\r", string.Join("|", s4), "display");
    }

    [Test]
    public static void Reset_state_goes_back_to_the_starting_values()
    {
        var def = SimProfiles.Create("projector")!;
        var (e, _, _) = Make(def);
        e.Receive(0, "PWR ON\r");
        Assert.Equal("1", e.Get("power")!, "on");
        e.ResetState();
        Assert.Equal("0", e.Get("power")!, "reset");
    }

    // ------------------------------------------------------------------ a simulator on a real program's COM port

    [Test]
    public static void A_simulator_answers_a_program_com_port_and_the_terminal_sees_both_ways()
    {
        var p = new Processor(Paths.TempData());
        p.AddLoader(new LpzLoader());
        using var _ = p;
        var slot = p.Slot(1);
        var def = SimProfiles.Create("projector")!;
        def.Com = "IP-ID-11/Slot-08/COM-01";
        var sim = slot.Io.Simulators.Add(def);
        p.LoadProgram(1, LpzPortTests.Build());
        var panel = slot.FindDevice(3) ?? throw new AssertException("panel 03");
        var key = new JoinKey(SigKind.Serial, 1);

        var port = slot.Io.Ports.Find("IP-ID-11/Slot-08/COM-01") ?? throw new AssertException("the port is listed once the program runs");
        Assert.True(port.Open, "open");
        Assert.True(sim.ComActive, "simulator knows the port is open");

        slot.Runtime!.TrySetSignal("#13", "PWR ON\r"); // the program sends on its COM port
        Assert.Eventually(() => (string)panel.GetFeedback(key) == "PWR=1\r", "the reply reached the program's receive signal");
        Assert.Equal("1", sim.Engine.Get("power")!, "simulator state");
        var t = port.Traffic.Since();
        Assert.True(t.Any(x => x.Dir == TrafficDir.Tx && x.Text == "PWR ON\r" && x.Origin == "program"), "TX recorded");
        Assert.True(t.Any(x => x.Dir == TrafficDir.Rx && x.Text == "PWR=1\r" && x.Origin == "simulator"), "RX recorded as from the simulator");
        Assert.Equal(7L, port.TxBytes, "TX bytes counted");

        Assert.True(port.ToProgramRx("typed\r", "terminal"), "terminal can send RX to the program");
        Assert.Eventually(() => (string)panel.GetFeedback(key) == "typed\r", "terminal text arrived");
        Assert.True(port.Traffic.Since().Any(x => x.Origin == "terminal"), "terminal RX recorded");

        slot.Unload();
        Assert.Equal(0, slot.Io.Ports.All.Count, "ports are forgotten on unload");
        Assert.True(!sim.ComActive, "simulator knows the port closed");
    }

    [Test]
    public static void Simulators_are_saved_per_slot_and_come_back_with_a_snapshot()
    {
        var dir = Paths.TempData();
        string id;
        using (var p = new Processor(dir, 2))
        {
            var def = SimProfiles.Create("dsp")!;
            def.Name = "Rack DSP"; def.Com = "COM-02";
            var h = p.Slot(2).Io.Simulators.Add(def);
            id = h.Def.Id;
            h.Engine.SetState("Mic1.vol", "12");
            p.Slot(2).Io.Redirects.Add(new RedirectEntry { From = "10.0.0.5:23", To = "sim:" + id });
            p.Slot(2).SetIpRemap(3, 0x0A);
        }
        using var q = new Processor(dir, 2);
        var got = q.Slot(2).Io.Simulators.Find(id) ?? throw new AssertException("simulator reloaded");
        Assert.Equal("Rack DSP", got.Def.Name, "name");
        Assert.Equal("COM-02", got.Def.Com, "attached port");
        Assert.Equal(0, q.Slot(1).Io.Simulators.All.Count, "other slot has none");
        Assert.Equal(1, q.Slot(2).Io.Redirects.List.Count, "redirect saved");
        Assert.Equal(0x0Au, q.Slot(2).PublicIpId(3), "remap saved");

        var snaps = new SlotSnapshots(dir);
        var info = snaps.Take(2, "x", "x.dll", SlotSnapshots.Manual) ?? throw new AssertException("snapshot");
        q.Slot(2).Io.Simulators.Remove(id);
        q.Slot(2).Io.Redirects.Remove(q.Slot(2).Io.Redirects.List[0].Id);
        q.Slot(2).ClearIpRemap(3);
        Assert.Equal(0, q.Slot(2).Io.Simulators.All.Count, "removed");
        snaps.Restore(2, info.Id);
        q.Slot(2).Io.Reload();
        Assert.Equal(1, q.Slot(2).Io.Simulators.All.Count, "simulator back from the snapshot");
        Assert.Equal(1, q.Slot(2).Io.Redirects.List.Count, "redirect back");
        Assert.Equal(0x0Au, q.Slot(2).PublicIpId(3), "remap back");
    }

    [Test]
    public static void A_tcp_simulator_listens_on_loopback_and_answers_a_client()
    {
        using var p = new Processor(Paths.TempData(), 1);
        var def = SimProfiles.Create("projector")!;
        def.Kind = "tcp"; def.Port = 0;
        var h = p.Slot(1).Io.Simulators.Add(def);
        Assert.True(h.Listening, "listening: " + h.Error);
        using var c = new System.Net.Sockets.TcpClient("127.0.0.1", h.ListenPort);
        var s = c.GetStream();
        s.ReadTimeout = 3000;
        s.Write(System.Text.Encoding.ASCII.GetBytes("PWR ON\r"));
        var buf = new byte[64]; var n = s.Read(buf, 0, buf.Length);
        Assert.Equal("PWR=1\r", System.Text.Encoding.ASCII.GetString(buf, 0, n), "reply over TCP");
        Assert.Equal("1", h.Engine.Get("power")!, "state");
        Assert.True(p.Slot(1).Io.Redirects.SimResolver!(h.Def.Id) is { } ep && ep.Port == h.ListenPort, "sim:ID resolves to its port");
    }
}
