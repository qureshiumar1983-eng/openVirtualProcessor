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

    private static (byte type, byte[] payload) Read(NetworkStream s)
    {
        var h = new byte[3];
        s.ReadExactly(h);
        var payload = new byte[(h[1] << 8) | h[2]];
        s.ReadExactly(payload);
        return (h[0], payload);
    }
}
