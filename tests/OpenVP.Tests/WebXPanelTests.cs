using System.Net;
using System.Net.WebSockets;
using System.Text;
using OpenVP.Core;
using OpenVP.Core.Security;
using OpenVP.Panels;
using OpenVP.SimplSharpHost;

namespace OpenVP.Tests;

/// <summary>CH5 clients (Crestron Virtual Panel, the HTML5 Web XPanel): CIP inside a WebSocket, and the token they ask for first.</summary>
public static class WebXPanelTests
{
    private static readonly Lazy<System.Security.Cryptography.X509Certificates.X509Certificate2> Cert =
        new(() => CertificateTools.CreateSelfSigned(["localhost"], [IPAddress.Loopback]));

    /// <summary>
    /// The sign-on a CH5 client sends, laid out as the community client siegeld/pycrestron builds it:
    /// [IP ID:2] 40 F1 01 [flags:4] [MAC:6] [make:50] [model:50] [Room ID:32] [hostname:64] [token length:2] [token].
    /// </summary>
    private static byte[] SignOn(uint ipId, string room = "", string? token = null)
    {
        var auth = token == null ? [] : Encoding.UTF8.GetBytes("Crestron:CSSelf:" + token);
        var p = new byte[213 + auth.Length];
        p[0] = (byte)(ipId >> 8); p[1] = (byte)ipId; p[2] = 0x40; p[3] = 0xF1; p[4] = 0x01;
        p[8] = (byte)(0x30 | (token != null ? 0x40 : 0) | (room.Length > 0 ? 0x01 : 0));
        Encoding.ASCII.GetBytes("Crestron").CopyTo(p, 15);
        Encoding.ASCII.GetBytes("WebXPanel").CopyTo(p, 65);
        Encoding.ASCII.GetBytes(room).CopyTo(p, 115);
        Encoding.ASCII.GetBytes("Hostname").CopyTo(p, 147);
        p[211] = (byte)(auth.Length >> 8); p[212] = (byte)auth.Length;
        auth.CopyTo(p, 213);
        return Cip.Frame(Cip.RoomConnectRequest, p);
    }

    private static ClientWebSocket Dial(WebXPanelServer server, bool tls = true, string path = "/websocket")
    {
        var ws = new ClientWebSocket();
        ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true; // self-signed, as on a processor
        using var cts = new CancellationTokenSource(5000);
        ws.ConnectAsync(new Uri($"{(tls ? "wss" : "ws")}://localhost:{server.Port}{path}"), cts.Token).GetAwaiter().GetResult();
        return ws;
    }

    /// <summary>One WebSocket message, which must be exactly one CIP packet.</summary>
    private static (byte type, byte[] payload) Read(ClientWebSocket ws)
    {
        var buf = new byte[4096];
        using var cts = new CancellationTokenSource(5000);
        var r = ws.ReceiveAsync(buf, cts.Token).GetAwaiter().GetResult();
        Assert.True(r.MessageType == WebSocketMessageType.Binary && r.EndOfMessage, "a whole binary message");
        var len = (buf[1] << 8) | buf[2];
        Assert.Equal(r.Count, 3 + len, "one CIP packet per WebSocket message");
        return (buf[0], buf.AsSpan(3, len).ToArray());
    }

    private static void Write(ClientWebSocket ws, byte[] packet) =>
        ws.SendAsync(packet, WebSocketMessageType.Binary, true, CancellationToken.None).GetAwaiter().GetResult();

    private static (Processor p, CipServer cip, WebXPanelServer web) Start(bool sample = false)
    {
        var p = new Processor(Paths.TempData());
        if (sample)
        {
            p.AddLoader(new SimplSharpLoader());
            p.LoadProgram(1, Paths.SampleDll);
        }
        var cip = new CipServer(p, 0, IPAddress.Loopback);
        cip.Start();
        var web = new WebXPanelServer(cip, 0, () => Cert.Value, IPAddress.Loopback);
        web.Start();
        return (p, cip, web);
    }

    [Test]
    public static void A_ch5_client_connects_over_a_secure_websocket_presses_and_gets_feedback()
    {
        var (p, cip, web) = Start(sample: true);
        using var _p = p; using var _c = cip; using var _w = web;
        using var ws = Dial(web);

        var ready = Read(ws);
        Assert.Equal(Cip.WhoAreYou, ready.type, "the server speaks first (program ready)");
        Assert.Equal("02", Convert.ToHexString(ready.payload), "ready");

        Write(ws, SignOn(0x03, token: "abc.def.ghi"));
        // Crestron Virtual Panel stops with "Event CONNECT_RESPONSE has no handler!" if this sign-on is answered with 0x02.
        var resp = Read(ws);
        Assert.Equal(Cip.RoomConnectResponse, resp.type, "the 0x26 sign-on is answered with 0x27 on a WebSocket");
        Assert.Equal(38, resp.payload.Length, "handle, mode, flags, extra flags and a 30-byte model");
        Assert.Equal("00000000", Convert.ToHexString(resp.payload, 0, 4), "handle 0 and result 0: Virtual Panel reads 03 there as 'Connection timed out'");
        Assert.Eventually(() => p.Slot(1).FindDevice(0x03)!.Online, "panel online in the program");

        // The client asks for every join's state, then presses d1; the sample program answers with feedback d1 high.
        Write(ws, Cip.Frame(Cip.Data, 0x00, 0x00, 0x02, 0x03, 0x00));
        Write(ws, Cip.Frame(Cip.Data, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00));
        var gotHigh = false;
        for (int i = 0; i < 40 && !gotHigh; i++)
        {
            var (type, payload) = Read(ws);
            if (Cip.DecodeJoin(type, payload) is var (k, v) && k == new JoinKey(SigKind.Digital, 1) && v is true) gotHigh = true;
        }
        Assert.True(gotHigh, "received d1 feedback over the WebSocket");

        Write(ws, Cip.Frame(Cip.HeartbeatRequest, 0x00, 0x00));
        var sawHeartbeat = false;
        for (int i = 0; i < 40 && !sawHeartbeat; i++) sawHeartbeat = Read(ws).type == Cip.HeartbeatResponse;
        Assert.True(sawHeartbeat, "heartbeat answered");

        var conn = p.Slot(1).FindDevice(0x03)!.Connections.Single();
        Assert.Equal("WebSocket (TLS)", conn.Details["Carried over"], "the IP table says how it is connected");
        Assert.Equal("WebXPanel", conn.Model, "and what the client called itself");
    }

    [Test]
    public static void A_ch5_client_is_also_served_without_tls_on_the_same_port_and_by_room_id()
    {
        var (p, cip, web) = Start();
        using var _p = p; using var _c = cip; using var _w = web;
        p.Slot(2).SetRoomId("Boardroom");
        var d1 = p.Slot(1).RegisterDevice(0x03, "XPanel", "a");
        var d2 = p.Slot(2).RegisterDevice(0x03, "XPanel", "b");

        using var ws = Dial(web, tls: false, path: "/");
        Read(ws);
        Write(ws, SignOn(0x03, room: "Boardroom"));
        Assert.Equal(Cip.RoomConnectResponse, Read(ws).type, "connect response");
        Assert.Eventually(() => d2.Online, "slot 2's panel online");
        Assert.True(!d1.Online, "slot 1's panel untouched");

        // Serial and analog as a CH5 client sends them: 0x15 with UTF-8 text and 0x01, both inside a 0x05 frame.
        var got = new List<(JoinKey, object)>();
        d2.InputReceived += (_, k, v) => { lock (got) got.Add((k, v)); };
        var text = Encoding.UTF8.GetBytes("Café");
        Write(ws, Cip.Frame(Cip.Data, [0x00, 0x00, (byte)(4 + text.Length), 0x15, 0x00, 0x04, 0x03, .. text]));
        Write(ws, Cip.Frame(Cip.Data, 0x00, 0x00, 0x05, 0x01, 0x00, 0x08, 0x12, 0x34));
        Write(ws, Cip.Frame(Cip.Data, 0x00, 0x00, 0x04, 0x01, 0x02, 0xAB, 0xCD));
        Assert.Eventually(() => { lock (got) return got.Count == 3; }, "three inputs arrived");
        Assert.Equal((new JoinKey(SigKind.Serial, 5), (object)"Café"), got[0], "serial");
        Assert.Equal((new JoinKey(SigKind.Analog, 9), (object)(ushort)0x1234), got[1], "analog, two-byte join");
        Assert.Equal((new JoinKey(SigKind.Analog, 3), (object)(ushort)0xABCD), got[2], "analog, one-byte join");

        // Feedback goes out one packet per message even when several are queued at once.
        for (uint j = 1; j <= 50; j++) d2.SetFeedback(new JoinKey(SigKind.Analog, j), (ushort)j);
        for (uint j = 1; j <= 50; j++)
        {
            var (type, payload) = Read(ws);
            Assert.Equal((new JoinKey(SigKind.Analog, j), (object)(ushort)j), Cip.DecodeJoin(type, payload)!.Value, "feedback in order");
        }
    }

    [Test]
    public static void The_token_a_ch5_client_asks_for_is_handed_out_over_https()
    {
        var (p, cip, web) = Start();
        using var _p = p; using var _c = cip; using var _w = web;
        using var http = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true }) { Timeout = TimeSpan.FromSeconds(5) };
        var root = $"https://localhost:{web.Port}";

        var login = http.GetAsync(root + "/userlogin.html").GetAwaiter().GetResult();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode, "the login page some clients visit first");
        Assert.True(login.Headers.GetValues("Set-Cookie").Any(c => c.StartsWith("TRACKID=")), "with its cookie");
        var posted = http.PostAsync(root + "/userlogin.html", new StringContent("login=admin&passwd=x")).GetAwaiter().GetResult();
        Assert.Equal(HttpStatusCode.OK, posted.StatusCode, "any login is accepted");

        var req = new HttpRequestMessage(HttpMethod.Get, root + "/cws/websocket/getWebSocketToken");
        req.Headers.Add("Origin", "file://");
        var resp = http.SendAsync(req).GetAwaiter().GetResult();
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode, "token");
        Assert.Equal(3, resp.Content.ReadAsStringAsync().GetAwaiter().GetResult().Split('.').Length, "shaped like a JWT");
        Assert.Equal("file://", resp.Headers.GetValues("Access-Control-Allow-Origin").Single(), "readable from a page loaded elsewhere");

        Assert.Equal(HttpStatusCode.NotFound, http.GetAsync(root + "/api/slots").GetAwaiter().GetResult().StatusCode, "nothing else is served here");
        Assert.True(p.RecentLog(1).Any(e => e.Message.Contains("asked for GET /cws/websocket/getWebSocketToken")), "what a client asked for is in the log");

        // The same answers without TLS.
        Assert.Equal(HttpStatusCode.OK, http.GetAsync($"http://localhost:{web.Port}/cws/websocket/getWebSocketToken").GetAwaiter().GetResult().StatusCode, "plain http");
    }

    [Test]
    public static void A_websocket_message_that_is_not_one_tidy_packet_does_not_derail_the_session()
    {
        var (p, cip, web) = Start();
        using var _p = p; using var _c = cip; using var _w = web;
        var d = p.Slot(1).RegisterDevice(0x03, "XPanel", "a");
        using var ws = Dial(web);
        Read(ws);
        Write(ws, SignOn(0x03));
        Read(ws);
        Assert.Eventually(() => d.Online, "online");

        var presses = new List<uint>();
        d.InputReceived += (_, k, _) => { lock (presses) presses.Add(k.Join); };
        ws.SendAsync(Encoding.UTF8.GetBytes("hello"), WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult(); // not CIP
        Write(ws, [0x05, 0x00, 0x40, 0x00, 0x00, 0x03, 0x00, 0x00, 0x00]);       // length field says more than the message holds
        Write(ws, [.. Cip.Digital(2, true), .. Cip.Digital(3, true)]);            // two packets in one message
        Write(ws, Cip.Digital(4, true));
        Assert.Eventually(() => { lock (presses) return presses.Count == 4; }, "all four presses arrived");
        Assert.Equal("1,2,3,4", string.Join(",", presses), "in order");
    }

    [Test]
    public static void An_unknown_room_is_refused_with_the_same_kind_of_answer_and_the_older_sign_on_still_gets_0x02()
    {
        var (p, cip, web) = Start();
        using var _p = p; using var _c = cip; using var _w = web;
        using var ws = Dial(web);
        Read(ws);
        Write(ws, SignOn(0x03, room: "NOSUCHROOM"));
        var refused = Read(ws);
        Assert.Equal(Cip.RoomConnectResponse, refused.type, "refused as 0x27");
        Assert.Equal("FFFF02", Convert.ToHexString(refused.payload), "refused");

        // A client that signs on the classic way over a WebSocket is answered the classic way.
        Write(ws, Cip.Frame(Cip.ConnectRequest, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x40, 0xFF, 0xFF, 0xF1, 0x01));
        Assert.Equal(Cip.ConnectResponse, Read(ws).type, "0x01 gets 0x02");
    }
}
