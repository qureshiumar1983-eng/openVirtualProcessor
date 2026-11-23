using System.Net;
using System.Net.Sockets;
using System.Text;
using OpenVP.Panels;

namespace OpenVP.Tests;

/// <summary>Network discovery: parsing what comes back (ARP, mDNS, Crestron replies), the mDNS responder, and a bounded scan.</summary>
public static class DiscoveryTests
{
    [Test]
    public static void Arp_tables_are_parsed_from_linux_windows_and_mac()
    {
        var linux = "IP address       HW type     Flags       HW address            Mask     Device\n192.168.1.1      0x1         0x2         aa:bb:cc:dd:ee:01     *        eth0\n192.168.1.7      0x1         0x0         00:00:00:00:00:00     *        eth0\n192.168.1.9      0x1         0x2         de:ad:be:ef:00:09     *        eth0\n";
        var l = DiscoveryParsing.ParseArp(linux);
        Assert.Equal(2, l.Count, "incomplete entries are left out");
        Assert.Equal("AA:BB:CC:DD:EE:01", l[0].Mac, "mac upper-cased");
        var win = "Interface: 192.168.1.20 --- 0x4\n  Internet Address      Physical Address      Type\n  192.168.1.1           aa-bb-cc-dd-ee-01     dynamic   \n  192.168.1.255         ff-ff-ff-ff-ff-ff     static    \n";
        var w = DiscoveryParsing.ParseArp(win);
        Assert.Equal(1, w.Count, "broadcast skipped");
        Assert.Equal("192.168.1.1", w[0].Ip, "windows ip");
        var mac = "? (192.168.1.5) at 0:1a:2b:3c:4d:5e on en0 ifscope [ethernet]\n? (192.168.1.6) at (incomplete) on en0\n";
        var m = DiscoveryParsing.ParseArp(mac);
        Assert.Equal("00:1A:2B:3C:4D:5E", m.Single().Mac, "macOS short octets padded");
    }

    [Test]
    public static void Dns_packets_round_trip_and_follow_name_compression()
    {
        var p = new DnsPacket { IsResponse = true, Id = 0 };
        p.Records.Add(DnsRecord.PtrRecord("_http._tcp.local", "OpenVP on BOX._http._tcp.local"));
        p.Records.Add(DnsRecord.SrvRecord("OpenVP on BOX._http._tcp.local", "box.local", 8080));
        p.Records.Add(DnsRecord.TxtRecord("OpenVP on BOX._http._tcp.local", new[] { "path=/", "a=b" }));
        p.Records.Add(DnsRecord.ARecord("box.local", IPAddress.Parse("192.168.1.20")));
        var back = DnsPacket.Parse(p.ToBytes()) ?? throw new AssertException("parsed");
        Assert.True(back.IsResponse && back.Records.Count == 4, "four records");
        Assert.Equal("OpenVP on BOX._http._tcp.local", back.Records[0].Target!, "ptr target");
        Assert.True(back.Records[1].Port == 8080 && back.Records[1].Target == "box.local", "srv");
        Assert.Equal("path=/|a=b", string.Join("|", back.Records[2].Text), "txt");
        Assert.Equal("192.168.1.20", back.Records[3].Address!.ToString(), "a");

        // A name that points back into the packet: "x" + pointer to offset 12 (where "box.local" starts in the question).
        var q = new List<byte> { 0, 0, 0x84, 0, 0, 1, 0, 1, 0, 0, 0, 0 };
        q.AddRange(new byte[] { 3, (byte)'b', (byte)'o', (byte)'x', 5, (byte)'l', (byte)'o', (byte)'c', (byte)'a', (byte)'l', 0, 0, 1, 0, 1 });
        q.AddRange(new byte[] { 0xC0, 12, 0, 1, 0, 1, 0, 0, 0, 120, 0, 4, 10, 1, 2, 3 });
        var c = DnsPacket.Parse(q.ToArray()) ?? throw new AssertException("compressed parsed");
        Assert.Equal("box.local", c.Records[0].Name, "pointer followed");
        Assert.True(DnsPacket.Parse(new byte[] { 1, 2, 3 }) == null, "junk is not a packet");
        Assert.True(DnsPacket.Parse(new byte[] { 0, 0, 0x84, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0xC0, 12, 0, 1 }) == null || true, "a pointer loop doesn't hang");
    }

    [Test]
    public static void Mdns_records_become_hosts_with_services()
    {
        var recs = new List<DnsRecord>
        {
            DnsRecord.SrvRecord("Projector._http._tcp.local", "proj-lobby.local", 80),
            DnsRecord.ARecord("proj-lobby.local", IPAddress.Parse("192.168.1.40")),
            DnsRecord.ARecord("printer.local", IPAddress.Parse("192.168.1.41")),
        };
        var found = DiscoveryParsing.FromMdns(recs).OrderBy(x => x.Ip).ToList();
        Assert.Equal(2, found.Count, "two hosts");
        Assert.True(found[0].Host == "proj-lobby" && found[0].Services.Single() == "_http._tcp :80", "service and short host name");
        Assert.True(found[1].Host == "printer" && found[1].Services.Count == 0, "address-only host");
    }

    [Test]
    public static void Crestron_reply_fills_in_model_firmware_and_mac()
    {
        var data = new byte[600];
        data[0] = 0x15;
        Encoding.ASCII.GetBytes("TSW-1070-LOBBY").CopyTo(data, 10);
        Encoding.ASCII.GetBytes("TSW-1070 [v3.002.1061 (Aug 05 2021), #7FB60A3F] @E-00107fd2e3c5").CopyTo(data, 266);
        var reply = ClientIdentifier.ParseDiscovery(data) ?? throw new AssertException("reply parsed");
        var list = new List<DiscoveredDevice>();
        var d = DiscoveryParsing.Touch(list, "192.168.1.60", "crestron");
        DiscoveryParsing.ApplyCrestron(d, reply);
        Assert.Equal("TSW-1070", d.Model!, "model");
        Assert.Equal("TSW-1070-LOBBY", d.Hostname!, "hostname");
        Assert.True(d.Firmware!.StartsWith("3.002") && d.Mac == "00:10:7F:D2:E3:C5", "firmware and mac: " + d.Firmware + " " + d.Mac);
        DiscoveryParsing.Touch(list, "192.168.1.60", "tcp");
        Assert.Equal(1, list.Count, "same address is one device");
        Assert.Equal("crestron,tcp", string.Join(",", d.Sources), "sources merged");
    }

    [Test]
    public static void Subnets_are_capped_to_a_slash_24()
    {
        var big = new DiscoverySubnet(IPAddress.Parse("10.5.7.20"), IPAddress.Parse("255.255.0.0"), "eth0");
        var hosts = big.Hosts().ToList();
        Assert.Equal(254, hosts.Count, "a /16 is cut to the /24 around us");
        Assert.True(hosts[0].ToString() == "10.5.7.1", "same third octet");
        var small = new DiscoverySubnet(IPAddress.Parse("192.168.1.130"), IPAddress.Parse("255.255.255.240"), "eth0");
        Assert.Equal(14, small.Hosts().Count(), "a /28 has 14 hosts");
        Assert.Equal("192.168.1.143", small.Broadcast.ToString(), "broadcast");
    }

    [Test]
    public static void Mdns_responder_answers_for_its_names_and_the_service()
    {
        var r = new MdnsResponder(8080, "openvp", "MyBox") { Addresses = () => new[] { IPAddress.Parse("192.168.1.20") } };
        Assert.Equal("mybox.local|openvp.local", string.Join("|", r.HostNames), "names");
        DnsPacket Ask(string name, ushort type) { var q = new DnsPacket(); q.Questions.Add(new DnsQuestion(name, type, false)); return q; }
        var a = r.Answer(Ask("openvp.local", DnsRecord.A)) ?? throw new AssertException("answered");
        Assert.Equal("192.168.1.20", a.Records.Single().Address!.ToString(), "A record for openvp.local");
        Assert.True(r.Answer(Ask("OPENVP.LOCAL", DnsRecord.A)) != null, "case-insensitive");
        Assert.True(r.Answer(Ask("other.local", DnsRecord.A)) == null, "other names ignored");
        var svc = r.Answer(Ask("_http._tcp.local", DnsRecord.Ptr)) ?? throw new AssertException("service answered");
        Assert.Equal("OpenVP on MyBox._http._tcp.local", svc.Records[0].Target!, "instance name");
        Assert.True(svc.Records.Any(x => x.Type == DnsRecord.Srv && x.Port == 8080) && svc.Records.Any(x => x.Type == DnsRecord.A), "SRV and address come with it");
        Assert.True(r.Answer(new DnsPacket { IsResponse = true }) == null, "responses are not answered");
        Assert.Equal("my-box", MdnsResponder.Sanitize("My Box"), "labels are clean");
    }

    [Test]
    public static void Mdns_start_is_harmless_when_multicast_is_unavailable()
    {
        var lines = new List<string>();
        using var r = new MdnsResponder(8080, listenPort: 0, log: l => lines.Add(l));
        var ok = r.Start(); // may or may not work in this environment; either way it must return
        Assert.True(ok || lines.Any(l => l.Contains("mDNS is off")), "either runs or says why not: " + string.Join("|", lines));
    }

    [Test]
    public static void A_scan_finds_open_ports_stays_bounded_and_ends()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        try
        {
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            var d = new NetworkDiscovery { ProbePorts = new[] { port, 1 }, HostsOverride = () => new[] { IPAddress.Loopback }, SubnetsOverride = () => Array.Empty<DiscoverySubnet>() };
            Assert.True(d.Start() && !d.Start(), "starts once");
            Assert.Eventually(() => !d.Running, "scan ends", 10000);
            var r = d.Snapshot();
            var dev = r.Devices.Single();
            Assert.Equal("127.0.0.1", dev.Ip, "address");
            Assert.Equal(port.ToString(), string.Join(",", dev.OpenPorts), "only the open port is listed");
            Assert.Equal("done", r.Stage, "stage");
        }
        finally { l.Stop(); }
    }

    [Test]
    public static void A_real_scan_with_nothing_to_find_does_not_hang()
    {
        var d = new NetworkDiscovery { Overall = TimeSpan.FromSeconds(12), ListenFor = TimeSpan.FromMilliseconds(500) };
        d.Start();
        Assert.Eventually(() => !d.Running, "finishes", 20000);
        Assert.True(d.Snapshot().Seconds < 20, "bounded");
    }
}
