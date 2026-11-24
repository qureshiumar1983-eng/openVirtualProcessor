using System.Net;
using System.Net.Sockets;
using OpenVP.Core;
using OpenVP.Panels;
using OpenVP.SimplSharpHost;

namespace OpenVP.Tests;

public static class CipTests
{
    [Test]
    public static void Digital_encoding_round_trips()
    {
        foreach (var (join, value) in new[] { (1u, true), (1u, false), (300u, true), (4000u, false) })
        {
            var f = Cip.Digital(join, value);
            var decoded = Cip.DecodeJoin(f[0], f.AsSpan(3));
            Assert.True(decoded.HasValue, "decoded");
            Assert.Equal(new JoinKey(SigKind.Digital, join), decoded!.Value.key, "join");
            Assert.Equal((object)value, decoded.Value.value, "value");
        }
    }

    [Test]
    public static void Analog_and_serial_encoding_round_trip()
    {
        var a = Cip.Analog(17, 54321);
        var da = Cip.DecodeJoin(a[0], a.AsSpan(3))!.Value;
        Assert.Equal(new JoinKey(SigKind.Analog, 17), da.key, "analog join");
        Assert.Equal((object)(ushort)54321, da.value, "analog value");

        var s = Cip.Serial(5, "Room A");
        var ds = Cip.DecodeJoin(s[0], s.AsSpan(3))!.Value;
        Assert.Equal(new JoinKey(SigKind.Serial, 5), ds.key, "serial join");
        Assert.Equal((object)"Room A", ds.value, "serial text");
    }

    [Test]
    public static void Simulated_panel_connects_presses_and_gets_feedback()
    {
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new SimplSharpLoader());
        p.LoadProgram(1, Paths.SampleDll);
        using var server = new CipServer(p, 0, IPAddress.Loopback);
        server.Start();

        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, server.Port);
        var s = client.GetStream();
        s.ReadTimeout = 3000;

        var who = Read(s);
        Assert.Equal(Cip.WhoAreYou, who.type, "server asks who we are");

        // Connect as IPID 03 (layout as community clients send it).
        s.Write(Cip.Frame(Cip.ConnectRequest, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x40, 0xFF, 0xFF, 0xF1, 0x01));
        var resp = Read(s);
        Assert.Equal(Cip.ConnectResponse, resp.type, "connect response");
        Assert.Eventually(() => p.Slot(1).FindDevice(0x03)!.Online, "panel online in program");

        // Press d1 -> program toggles feedback d1 -> we receive digital d1 high.
        s.Write(Cip.Digital(1, true));
        var gotHigh = false;
        for (int i = 0; i < 20 && !gotHigh; i++)
        {
            var (type, payload) = Read(s);
            if (Cip.DecodeJoin(type, payload) is var (k, v) && k == new JoinKey(SigKind.Digital, 1) && v is true) gotHigh = true;
        }
        Assert.True(gotHigh, "received d1 feedback over CIP");

        s.Write(Cip.Frame(Cip.HeartbeatRequest, 0x00, 0x00));
        var sawHeartbeat = false;
        for (int i = 0; i < 20 && !sawHeartbeat; i++) sawHeartbeat = Read(s).type == Cip.HeartbeatResponse;
        Assert.True(sawHeartbeat, "heartbeat answered");
    }

    [Test]
    public static void Feedback_burst_arrives_complete_and_in_order()
    {
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new SimplSharpLoader());
        p.LoadProgram(1, Paths.SampleDll);
        using var server = new CipServer(p, 0, IPAddress.Loopback);
        server.Start();
        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, server.Port);
        var s = client.GetStream();
        s.ReadTimeout = 3000;
        Read(s); // who are you
        s.Write(Cip.Frame(Cip.ConnectRequest, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x40, 0xFF, 0xFF, 0xF1, 0x01));
        Assert.Equal(Cip.ConnectResponse, Read(s).type, "connect response");
        var dev = p.Slot(1).FindDevice(0x03)!;
        Assert.Eventually(() => dev.Online, "panel online");

        const int n = 3000;
        for (uint j = 1; j <= n; j++) dev.SetFeedback(new JoinKey(SigKind.Analog, 100 + j), (ushort)j); // one burst, many packets per write
        uint next = 1;
        while (next <= n)
        {
            var (type, payload) = Read(s);
            if (Cip.DecodeJoin(type, payload) is var (k, v) && k.Kind == SigKind.Analog && k.Join > 100 && k.Join <= 100 + n)
            {
                Assert.Equal(100 + next, k.Join, "analog join order");
                Assert.Equal((ushort)next, (ushort)v, "analog value");
                next++;
            }
        }
    }

    // A panel that connects with a tiny receive window and then does not read: every write to it blocks.
    private static (TcpClient client, NetworkStream stream, JoinDevice dev) StalledPanel(Processor p, CipServer server, int joins, int textLen)
    {
        var dev = DeviceDirectory.Resolve(p, 3, null);
        var text = new string('x', textLen);
        for (uint j = 1; j <= joins; j++) dev.SetFeedback(new JoinKey(SigKind.Serial, j), text);
        var client = new TcpClient { ReceiveBufferSize = 1024 }; // before connecting, so the window stays small
        client.Connect(IPAddress.Loopback, server.Port);
        var s = client.GetStream();
        s.ReadTimeout = 10_000;
        Read(s); // who are you
        s.Write(Cip.Frame(Cip.ConnectRequest, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x40, 0xFF, 0xFF, 0xF1, 0x01));
        Assert.Equal(Cip.ConnectResponse, Read(s).type, "connect response");
        s.Write(Cip.Frame(Cip.Data, 0x00, 0x00, 0x02, Cip.SubUpdateRequest, 0x00)); // ask for the snapshot, then stop reading
        return (client, s, dev);
    }

    [Test]
    public static void A_session_whose_write_is_stuck_still_cleans_up_when_it_ends()
    {
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new SimplSharpLoader());
        p.LoadProgram(1, Paths.SampleDll);
        var server = new CipServer(p, 0, IPAddress.Loopback);
        server.Start();
        var (client, _, dev) = StalledPanel(p, server, 15_000, 240); // ~3.6 MB: more than the socket buffers hold
        using var _c = client;
        Assert.Eventually(() => dev.ConnectionCount == 1 && server.SessionCount == 1, "panel attached");
        Thread.Sleep(500); // let the sender fill the socket and block in its write
        server.Dispose(); // the server shuts down: the read loop ends while the sender is still stuck
        Assert.Eventually(() => server.SessionCount == 0, "the session was removed even though its write was stuck");
        Assert.Equal(0, dev.ConnectionCount, "device connection released");
    }

    [Test]
    public static void The_snapshot_of_a_device_with_more_joins_than_the_queue_limit_is_not_a_disconnect()
    {
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new SimplSharpLoader());
        p.LoadProgram(1, Paths.SampleDll);
        using var server = new CipServer(p, 0, IPAddress.Loopback);
        server.Start();
        const int joins = 45_000; // more than the 20,000-packet queue limit
        var (client, s, dev) = StalledPanel(p, server, joins, 240);
        using var _c = client;
        Thread.Sleep(1000); // the whole snapshot is queued while the panel reads nothing
        Assert.Equal(1, dev.ConnectionCount, "panel still attached after the snapshot was queued");
        var serials = 0;
        while (true)
        {
            var (type, payload) = Read(s);
            if (Cip.DecodeJoin(type, payload) is var (k, _) && k.Kind == SigKind.Serial) serials++;
            if (type == Cip.Data && payload.Length == 5 && payload[3] == 0x03 && payload[4] == 0x16) break; // update complete
        }
        Assert.Equal(joins, serials, "every join of the snapshot arrived");
    }

    [Test]
    public static void Smart_object_joins_round_trip_for_all_three_kinds()
    {
        foreach (var so in new[] { 1u, 25u, 401u })
        {
            var cases = new (JoinKey key, object value)[]
            {
                (new JoinKey(SigKind.Digital, 2011, so), true),
                (new JoinKey(SigKind.Digital, 4017, so), false),
                (new JoinKey(SigKind.Analog, 14, so), (ushort)54321),
                (new JoinKey(SigKind.Serial, 12, so), "Row 3"),
            };
            foreach (var (key, value) in cases)
                foreach (var fromPanel in new[] { false, true })
                {
                    var f = Cip.Encode(key, value, fromPanel);
                    var d = Cip.DecodeJoin(f[0], f.AsSpan(3));
                    Assert.True(d.HasValue, $"decoded {key}");
                    Assert.Equal(key, d!.Value.key, "key incl. smart object");
                    Assert.Equal(so, d.Value.key.SmartObject, "smart object id");
                    Assert.Equal(value, d.Value.value, "value");
                }
        }
    }

    [Test]
    public static void Smart_object_wire_layout_and_packed_joins()
    {
        // 05 | 00 00 | 09 38 | 00 00 01 91 (id 401) | 03 27 | 0A 00 (join 11 high)
        var press = Cip.SmartDigital(401, 11, true, press: true);
        Assert.Equal("05 000C 0000 09 38 00000191 03 27 0A00".Replace(" ", ""), Convert.ToHexString(press), "digital press bytes");
        // A processor can pack several joins into one sub-packet: three digitals of smart object 2.
        var inner = new byte[] { 0x07, 0x00, 0x00, 0x00, 0x00, 0x01, 0x01, 0x80 }; // type 00, three pairs
        var body = new byte[] { 0, 0, 0, 2 }.Concat(inner).ToArray();
        var payload = new byte[] { 0x00, 0x00, (byte)(1 + body.Length), 0x38 }.Concat(body).ToArray();
        var all = Cip.Decode(Cip.Data, payload);
        Assert.Equal(3, all.Count, "three digitals from one sub-packet");
        Assert.Equal(new JoinKey(SigKind.Digital, 1, 2), all[0].key, "first");
        Assert.Equal(new JoinKey(SigKind.Digital, 257, 2), all[1].key, "second");
        Assert.Equal((object)false, all[2].value, "third is low");
    }

    [Test]
    public static void Connect_request_ip_id_is_read_from_both_sign_on_forms()
    {
        Assert.Equal((uint?)0x03, Cip.DecodeConnectIpId(Cip.ConnectRequest, new byte[] { 0, 0, 0, 0, 0, 0x03, 0x40, 0xFF, 0xFF, 0xF1, 0x01 }), "long sign-on");
        Assert.Equal((uint?)0x04, Cip.DecodeConnectIpId(Cip.ConnectRequest, new byte[] { 0x7F, 0, 0, 1, 0, 0x04, 0x40 }), "older long sign-on");
        // Short sign-on (XPanels, apps): the IP ID is the second byte, not the one before the 0x40.
        Assert.Equal((uint?)0x05, Cip.DecodeConnectIpId(Cip.ConnectRequestAlt, new byte[] { 0x00, 0x05, 0xA3, 0x42, 0x40, 0x02, 0, 0, 0, 0 }), "short sign-on, 10 bytes");
        Assert.Equal((uint?)0x06, Cip.DecodeConnectIpId(Cip.ConnectRequestAlt, new byte[] { 0x00, 0x06, 0xA3, 0x42, 0x40, 0x02, 0, 0, 0xD1, 0x01, 0 }), "short sign-on, 11 bytes");
        Assert.True(Cip.DecodeConnectIpId(Cip.Data, new byte[] { 0, 0, 3, 0, 0, 0 }) == null, "data is not a sign-on");
    }

    [Test]
    public static void Short_sign_on_binds_to_the_right_ip_id()
    {
        using var p = new Processor(Paths.TempData());
        p.Slot(1).RegisterDevice(0x05, "XPanel", "x");
        using var server = new CipServer(p, 0, IPAddress.Loopback);
        server.Start();
        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, server.Port);
        var s = client.GetStream();
        s.ReadTimeout = 3000;
        Read(s);
        s.Write(Cip.Frame(Cip.ConnectRequestAlt, 0x00, 0x05, 0xA3, 0x42, 0x40, 0x02, 0x00, 0x00, 0x00, 0x00));
        Assert.Equal(Cip.ConnectResponse, Read(s).type, "connect response");
        Assert.Eventually(() => p.Slot(1).FindDevice(0x05)!.Online, "IP ID 05 online");
        Assert.True(p.Slot(1).FindDevice(0x40) == null, "nothing bound to the 0x40 marker byte");
    }

    [Test]
    public static void An_unknown_handshake_is_logged_with_its_bytes()
    {
        using var p = new Processor(Paths.TempData());
        using var server = new CipServer(p, 0, IPAddress.Loopback); // packet logging off
        server.Start();
        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, server.Port);
        var s = client.GetStream();
        s.ReadTimeout = 3000;
        Read(s);
        for (int i = 0; i < 10; i++) s.Write(Cip.Frame(0x31, 0x00, 0x03, 0x40, 0xAB));
        s.Write(Cip.Frame(Cip.HeartbeatRequest, 0x00, 0x00));
        Assert.Equal(Cip.HeartbeatResponse, Read(s).type, "still answers heartbeats");
        var lines = p.RecentLog(1).Where(e => e.Message.Contains("31 000340AB")).ToList();
        Assert.Equal(4, lines.Count, "logged, but only the first few");
        Assert.True(lines[0].Message.Contains("report"), "first line asks for the bytes");
    }

    // The sign-on a Crestron XPanel sent to OpenVP with IP ID 03 and Room ID SLOT01, as logged.
    private const string XPanelRoomSignOn =
        "000340F101000000010232353F7516"
        + "4372657374726F6E000000000000000000000000000000000000000000000000000000000000000000000000000000000000"
        + "5850616E656C0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000"
        + "534C4F5430310000000000000000000000000000000000000000000000000000"
        + "5850616E656C202D2030322D33322D33352D33462D37352D3136000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000";

    private static byte[] RoomSignOn(uint ipId, string room)
    {
        var b = Convert.FromHexString(XPanelRoomSignOn);
        b[1] = (byte)ipId;
        Array.Clear(b, 115, 32);
        System.Text.Encoding.ASCII.GetBytes(room).CopyTo(b, 115);
        return Cip.Frame(Cip.RoomConnectRequest, b);
    }

    [Test]
    public static void Room_sign_on_from_an_xpanel_decodes()
    {
        var rc = Cip.DecodeRoomConnect(Cip.RoomConnectRequest, Convert.FromHexString(XPanelRoomSignOn))!;
        Assert.Equal(3u, rc.IpId, "IP ID");
        Assert.Equal("SLOT01", rc.RoomId, "Room ID");
        Assert.Equal("Crestron", rc.Make, "make");
        Assert.Equal("XPanel", rc.Model, "model");
        Assert.Equal("XPanel - 02-32-35-3F-75-16", rc.Hostname, "hostname");
        Assert.Equal("02:32:35:3F:75:16", rc.Mac, "MAC");
        Assert.True(Cip.DecodeRoomConnect(Cip.RoomConnectRequest, new byte[] { 0, 3, 0x40 }) == null, "too short");
        Assert.True(Cip.DecodeRoomConnect(Cip.ConnectRequest, Convert.FromHexString(XPanelRoomSignOn)) == null, "other type");
        var full = new byte[211];
        Array.Fill(full, (byte)'A', 115, 32);
        Assert.Equal(new string('A', 32), Cip.DecodeRoomConnect(Cip.RoomConnectRequest, full)!.RoomId, "a 32-character Room ID has no NUL after it");
    }

    private static NetworkStream Dial(CipServer server, out TcpClient client)
    {
        client = new TcpClient();
        client.Connect(IPAddress.Loopback, server.Port);
        var s = client.GetStream();
        s.ReadTimeout = 3000;
        Read(s);
        return s;
    }

    [Test]
    public static void Two_slots_with_the_same_ip_id_are_told_apart_by_room_id()
    {
        using var p = new Processor(Paths.TempData(), 3);
        p.Slot(2).SetRoomId("Boardroom");
        var d1 = p.Slot(1).RegisterDevice(0x03, "XPanel", "a");
        var d2 = p.Slot(2).RegisterDevice(0x03, "XPanel", "b");
        using var server = new CipServer(p, 0, IPAddress.Loopback);
        server.Start();

        var s2 = Dial(server, out var c2);
        using var _2 = c2;
        s2.Write(RoomSignOn(3, "BOARDROOM")); // not case-sensitive
        Assert.Equal("0000001F", Convert.ToHexString(Read(s2).payload), "accepted");
        Assert.Eventually(() => d2.Online, "slot 2's panel online");
        Assert.True(!d1.Online, "slot 1's panel untouched");

        var s1 = Dial(server, out var c1);
        using var _1 = c1;
        s1.Write(RoomSignOn(3, "SLOT01"));
        Read(s1);
        Assert.Eventually(() => d1.Online, "slot 1's panel online");

        // A press on the slot 2 panel reaches slot 2 only.
        var got1 = 0; var got2 = 0;
        d1.InputReceived += (_, _, _) => Interlocked.Increment(ref got1);
        d2.InputReceived += (_, _, _) => Interlocked.Increment(ref got2);
        s2.Write(Cip.Digital(7, true));
        Assert.Eventually(() => Volatile.Read(ref got2) == 1, "press reached slot 2");
        Assert.Equal(0, Volatile.Read(ref got1), "and not slot 1");
    }

    [Test]
    public static void Room_sign_on_with_an_unknown_room_is_refused_and_logged()
    {
        using var p = new Processor(Paths.TempData(), 2);
        using var server = new CipServer(p, 0, IPAddress.Loopback);
        server.Start();
        var s = Dial(server, out var c);
        using var _ = c;
        s.Write(RoomSignOn(3, "NOPE"));
        var r = Read(s);
        Assert.Equal(Cip.RoomConnectResponse, r.type, "answered");
        Assert.Equal("FFFF02", Convert.ToHexString(r.payload), "refused");
        Assert.True(p.RecentLog(1).Any(e => e.Message.Contains("'NOPE'") && e.Message.Contains("SLOT02")), "log names the room and lists the ones that exist");
        Assert.True(p.Slot(1).FindDevice(0x03) == null, "no device created");
        // The panel can correct itself on the same connection.
        s.Write(RoomSignOn(3, "SLOT02"));
        Assert.Equal("0000001F", Convert.ToHexString(Read(s).payload), "accepted the second time");
        Assert.Eventually(() => p.Slot(2).FindDevice(0x03)?.Online == true, "online in slot 2");
    }

    [Test]
    public static void Room_answers_are_tried_in_turn_until_a_panel_carries_on()
    {
        using var p = new Processor(Paths.TempData(), 2);
        p.Slot(2).RegisterDevice(0x03, "XPanel", "x");
        using var server = new CipServer(p, 0, IPAddress.Loopback);
        server.Start();
        // A panel that hangs up on every answer but the third: two attempts each, as the server gives each answer.
        string Attempt(bool carryOn)
        {
            var s = Dial(server, out var c);
            using var _ = c;
            s.Write(RoomSignOn(3, "SLOT02"));
            var (type, payload) = Read(s);
            var seen = $"{type:X2} {Convert.ToHexString(payload)}";
            if (carryOn)
            {
                Assert.Equal(Cip.WhoAreYou, Read(s).type, "third answer ends with program ready");
                s.Write(Cip.Frame(Cip.ConnectRequest, 0, 0, 0, 0, 0, 0x03, 0x40, 0xFF, 0xFF, 0xF1, 0x01)); // no Room ID in this one
                Assert.Equal(Cip.ConnectResponse, Read(s).type, "second sign-on answered");
                Assert.Eventually(() => p.Slot(2).FindDevice(0x03)!.Online, "still bound to the room's slot, not slot 1");
                Assert.True(p.Slot(1).FindDevice(0x03) == null, "nothing in slot 1");
            }
            var before = server.SessionCount;
            c.Close();
            Assert.Eventually(() => server.SessionCount < before || server.SessionCount == 0, "session ended");
            return seen;
        }
        Assert.Equal("02 0000001F", Attempt(false), "first answer");
        Assert.Equal("02 0000001F", Attempt(false), "first answer, second try");
        Assert.Equal("27 000000", Attempt(false), "second answer");
        Assert.Equal("27 000000", Attempt(false), "second answer, second try");
        Assert.Equal("27 0000001F", Attempt(true), "third answer");
        Assert.Equal("27 0000001F", Attempt(false), "kept once accepted");
        Assert.Equal("27 0000001F", Attempt(false), "and not changed by later hang-ups");
        Assert.True(p.RecentLog().Any(e => e.Message.Contains("accepted Room ID answer 3 of")), "the accepted answer is logged");
    }

    [Test]
    public static void Room_sign_on_without_a_room_id_routes_by_ip_id()
    {
        using var p = new Processor(Paths.TempData(), 2);
        p.Slot(2).RegisterDevice(0x09, "XPanel", "x");
        using var server = new CipServer(p, 0, IPAddress.Loopback);
        server.Start();
        var s = Dial(server, out var c);
        using var _ = c;
        s.Write(RoomSignOn(9, ""));
        Read(s);
        Assert.Eventually(() => p.Slot(2).FindDevice(0x09)!.Online, "found by IP ID");
    }

    [Test]
    public static void Unknown_data_sub_types_are_reported()
    {
        var unknown = new List<(byte sub, byte[] raw)>();
        var joins = Cip.Decode(Cip.Data, new byte[] { 0x00, 0x00, 0x03, 0x77, 0xAA, 0xBB, 0x03, 0x00, 0x00, 0x00 }, unknown);
        Assert.Equal(1, joins.Count, "the join after the unknown sub-packet still decodes");
        Assert.Equal(1, unknown.Count, "one unknown");
        Assert.Equal((byte)0x77, unknown[0].sub, "sub-type");
    }

    [Test]
    public static void Smart_object_feedback_and_presses_travel_over_cip()
    {
        using var p = new Processor(Paths.TempData());
        p.AddLoader(new SimplSharpLoader());
        p.LoadProgram(1, Paths.SampleDll);
        using var server = new CipServer(p, 0, IPAddress.Loopback);
        server.Start();
        var dev = p.Slot(1).FindDevice(0x03)!;
        var so = 7u;
        dev.SetFeedback(new JoinKey(SigKind.Digital, 2011, so), true);   // set before the panel connects: arrives in the snapshot
        dev.SetFeedback(new JoinKey(SigKind.Analog, 14, so), (ushort)3);
        dev.SetFeedback(new JoinKey(SigKind.Serial, 12, so), "Item A");

        using var client = new TcpClient();
        client.Connect(IPAddress.Loopback, server.Port);
        var s = client.GetStream();
        s.ReadTimeout = 3000;
        Read(s); // who are you
        s.Write(Cip.Frame(Cip.ConnectRequest, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x40, 0xFF, 0xFF, 0xF1, 0x01));
        Assert.Equal(Cip.ConnectResponse, Read(s).type, "connect response");
        Assert.Eventually(() => dev.Online, "panel online");
        s.Write(Cip.Frame(Cip.Data, 0x00, 0x00, 0x02, Cip.SubUpdateRequest, 0x00));

        var seen = new Dictionary<JoinKey, object>();
        while (true)
        {
            var (type, payload) = Read(s);
            foreach (var (k, v) in Cip.Decode(type, payload)) seen[k] = v;
            if (type == Cip.Data && payload.Length == 5 && payload[3] == 0x03 && payload[4] == 0x16) break; // update complete
        }
        Assert.Equal((object)true, seen[new JoinKey(SigKind.Digital, 2011, so)], "smart digital in snapshot");
        Assert.Equal((object)(ushort)3, seen[new JoinKey(SigKind.Analog, 14, so)], "smart analog in snapshot");
        Assert.Equal((object)"Item A", seen[new JoinKey(SigKind.Serial, 12, so)], "smart serial in snapshot");

        // A live change after connect.
        dev.SetFeedback(new JoinKey(SigKind.Digital, 2012, so), true);
        var live = false;
        for (int i = 0; i < 20 && !live; i++)
        {
            var (type, payload) = Read(s);
            live = Cip.Decode(type, payload).Any(j => j.key == new JoinKey(SigKind.Digital, 2012, so) && j.value is true);
        }
        Assert.True(live, "live smart object feedback arrived");

        // The panel presses an item, sends an analog and a serial.
        s.Write(Cip.Encode(new JoinKey(SigKind.Digital, 13, 1), true, fromPanel: true));
        s.Write(Cip.Encode(new JoinKey(SigKind.Analog, 5, so), (ushort)99, fromPanel: true));
        s.Write(Cip.Encode(new JoinKey(SigKind.Serial, 6, so), "typed", fromPanel: true));
        Assert.Eventually(() => dev.GetInput(new JoinKey(SigKind.Digital, 13, 1)) is true, "smart press reached the device");
        Assert.Eventually(() => dev.GetInput(new JoinKey(SigKind.Analog, 5, so)) is ushort a && a == 99, "smart analog reached the device");
        Assert.Eventually(() => dev.GetInput(new JoinKey(SigKind.Serial, 6, so)) is "typed", "smart serial reached the device");
        Assert.Equal((object)false, dev.GetInput(new JoinKey(SigKind.Digital, 13)), "not misread as a plain join");
    }

    private static (byte type, byte[] payload) Read(NetworkStream s)
    {
        var h = new byte[3];
        s.ReadExactly(h);
        var payload = new byte[(h[1] << 8) | h[2]];
        s.ReadExactly(payload);
        return (h[0], payload);
    }
}
