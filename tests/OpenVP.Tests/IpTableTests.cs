using System.Net;
using System.Net.Sockets;
using System.Text;
using OpenVP.Core;
using OpenVP.Panels;
using OpenVP.SimplSharpHost;

namespace OpenVP.Tests;

/// <summary>The IP table's "who is connected": connection records, identification, EISC peers.</summary>
public static class IpTableTests
{
    private static byte[] FakeDiscoveryReply(string hostname, string description)
    {
        var p = new byte[266 + 256];
        p[0] = 0x15;
        Encoding.ASCII.GetBytes(hostname).CopyTo(p, 10);
        Encoding.ASCII.GetBytes(description).CopyTo(p, 266);
        return p;
    }

    [Test]
    public static void Discovery_reply_gives_hostname_model_firmware_and_mac()
    {
        var r = ClientIdentifier.ParseDiscovery(FakeDiscoveryReply("TSW-1070-LOBBY",
            "TSW-1070 [v3.002.1061 (Thu Aug 05 11:03:28 2021), #7FB60A3F] @E-00107fd2e3c5"))!;
        Assert.Equal("TSW-1070-LOBBY", r.Hostname, "hostname");
        Assert.Equal("TSW-1070", r.Model, "model");
        Assert.Equal("3.002.1061", r.Firmware, "firmware");
        Assert.Equal("00:10:7F:D2:E3:C5", r.Mac, "mac");

        var c = new DeviceConnection("CIP", "10.0.0.5", 50000);
        ClientIdentifier.ApplyDiscovery(c, r);
        Assert.Equal("Crestron touch panel (TSW-1070)", c.ClientKind, "kind");
        Assert.True(c.Details.ContainsKey("Autodiscovery"), "raw description kept");
    }

    [Test]
    public static void Discovery_ignores_queries_and_garbage()
    {
        Assert.True(ClientIdentifier.ParseDiscovery(ClientIdentifier.DiscoveryQuery("me")) == null, "our own query is not a reply");
        Assert.True(ClientIdentifier.ParseDiscovery(new byte[] { 0x15, 0, 0 }) == null, "too short");
    }

    [Test]
    public static void Discovery_asks_over_udp_and_reads_the_answer()
    {
        using var responder = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)responder.Client.LocalEndPoint!).Port;
        var answered = Task.Run(async () =>
        {
            var q = await responder.ReceiveAsync();
            Assert.Equal((byte)0x14, q.Buffer[0], "query type");
            var reply = FakeDiscoveryReply("CP4-OFFICE", "CP4 [v2.8001.00099 (Mar 01 2024), #00000000] @E-00107f000001");
            await responder.SendAsync(reply, reply.Length, q.RemoteEndPoint);
        });
        var r = ClientIdentifier.DiscoverAsync(IPAddress.Loopback, default, port).GetAwaiter().GetResult();
        answered.Wait(3000);
        Assert.True(r != null, "got a reply");
        Assert.Equal("CP4-OFFICE", r!.Hostname, "hostname");
        Assert.Equal("CP4", r.Model, "model");
        Assert.Equal("Crestron control processor (CP4)", ClientIdentifier.DescribeModel(r.Model, "CIP"), "kind");
    }

    [Test]
    public static void User_agent_tells_browser_and_os()
    {
        var c = new DeviceConnection("Web panel", "10.0.0.9", 1);
        ClientIdentifier.ApplyUserAgent(c, "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        Assert.Equal("Browser panel (Chrome 131 on Windows 10/11)", c.ClientKind, "desktop chrome");

        var ipad = new DeviceConnection("Web panel", "10.0.0.10", 1);
        ClientIdentifier.ApplyUserAgent(ipad, "Mozilla/5.0 (iPad; CPU OS 17_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Mobile/15E148 Safari/604.1");
        Assert.Equal("iPadOS", ipad.GetDetail("OS"), "ipad os");
        Assert.Equal("Safari 17", ipad.GetDetail("Browser"), "ipad browser");
    }

    [Test]
    public static void Cip_panel_shows_up_in_the_ip_table_with_its_address_and_counters()
    {
        ClientIdentifier.NetworkProbesEnabled = false; // loopback is identified without the network anyway
        try
        {
            using var p = new Processor(Paths.TempData());
            p.AddLoader(new SimplSharpLoader());
            p.LoadProgram(1, Paths.SampleDll);
            using var server = new CipServer(p, 0, IPAddress.Loopback);
            server.Start();

            using var client = new TcpClient();
            client.Connect(IPAddress.Loopback, server.Port);
            var s = client.GetStream();
            s.Write(Cip.Frame(Cip.ConnectRequest, 0x00, 0x00, 0x00, 0x00, 0x00, 0x03, 0x40, 0xFF, 0xFF, 0xF1, 0x01));
            var dev = p.Slot(1).FindDevice(0x03)!;
            Assert.Eventually(() => dev.Connections.Count == 1, "connection listed");

            var c = dev.Connections[0];
            Assert.Equal("CIP", c.Transport, "transport");
            Assert.Equal("127.0.0.1", c.RemoteAddress, "remote address");
            Assert.True(c.RemotePort > 0, "remote port");
            Assert.Eventually(() => c.Hostname == Environment.MachineName, "loopback client named after this computer");
            Assert.Equal("XPanel or app on this computer", c.ClientKind, "kind");
            Assert.True(c.GetDetail("Connect request")?.StartsWith("01 ") == true, "connect request kept");

            s.Write(Cip.Digital(10, true));
            s.Write(Cip.Frame(Cip.HeartbeatRequest, 0x00, 0x00));
            Assert.Eventually(() => Interlocked.Read(ref c.JoinsIn) == 1 && Interlocked.Read(ref c.Heartbeats) == 1, "press and heartbeat counted");
            Assert.True(Interlocked.Read(ref c.BytesIn) > 0 && Interlocked.Read(ref c.MessagesOut) > 0, "traffic counted");
            Assert.True(p.Slot(1).RunConsoleCommand("ipt").Contains("127.0.0.1"), "ipt lists the client address");

            client.Close();
            Assert.Eventually(() => dev.Connections.Count == 0 && !dev.Online, "connection removed on disconnect");
            Assert.True(dev.LastOffline != null && dev.TotalConnects == 1, "history kept");
        }
        finally { ClientIdentifier.NetworkProbesEnabled = true; }
    }

    [Test]
    public static void Eisc_link_names_the_slot_on_the_other_end()
    {
        using var p = new Processor(Paths.TempData());
        var a = p.Slot(1).RegisterDevice(0x10, "EISC", "to slot 2");
        var b = p.Slot(2).RegisterDevice(0x10, "EISC", "to slot 1");
        p.Bus.Attach(a);
        p.Bus.Attach(b);
        Assert.Equal(1, a.Connections.Count, "slot 1 side has one peer");
        Assert.Equal("slot 2", a.Connections[0].Peer, "peer named");
        Assert.Equal("slot 1", b.Connections[0].Peer, "other side named");

        a.SetFeedback(new JoinKey(SigKind.Digital, 1), true);
        Assert.Equal(1L, Interlocked.Read(ref a.Connections[0].JoinsOut), "join out counted");
        Assert.Equal(1L, Interlocked.Read(ref b.Connections[0].JoinsIn), "join in counted");

        p.Bus.Detach(b);
        Assert.True(!a.Online && a.Connections.Count == 0, "unlinked");
    }

    [Test]
    public static void Count_only_attach_still_works()
    {
        var d = new JoinDevice(1, 3, "Panel", "");
        d.AttachConnection();
        d.AttachConnection();
        Assert.Equal(2, d.ConnectionCount, "two attached");
        d.DetachConnection();
        Assert.True(d.Online, "still online");
        d.DetachConnection();
        Assert.True(!d.Online && d.Connections.Count == 0, "offline");
        d.DetachConnection(); // extra detach is harmless
        Assert.Equal(0, d.ConnectionCount, "never negative");
    }
}
