using System.Net;
using OpenVP.Core;
using OpenVP.Panels;

namespace OpenVP.Tests;

/// <summary>Network redirects, the IP ID remap, and the socket table.</summary>
public static class DeviceNetTests
{
    private static NetworkRedirects Make() => new(Path.Combine(Paths.TempData(), "redirects.json"));

    [Test]
    public static void Redirect_matches_host_and_port_most_specific_first()
    {
        var r = Make();
        r.Add(new RedirectEntry { From = "192.168.1.50", To = "127.0.0.1:9001", Note = "any port" });
        r.Add(new RedirectEntry { From = "192.168.1.50:23", To = "127.0.0.1:9002" });
        var a = r.Resolve("192.168.1.50", 23);
        Assert.Equal("127.0.0.1:9002", $"{a.Host}:{a.Port}", "the entry with a port wins");
        var b = r.Resolve("192.168.1.50", 80);
        Assert.Equal("127.0.0.1:9001", $"{b.Host}:{b.Port}", "host-only entry covers other ports");
        var c = r.Resolve("192.168.1.51", 23);
        Assert.True(!c.Redirected && c.Host == "192.168.1.51" && c.Port == 23, "other hosts untouched");
        Assert.True(r.Resolve("192.168.1.50".ToUpperInvariant(), 23).Redirected, "host compare ignores case");
    }

    [Test]
    public static void Redirect_honours_protocol_enabled_and_wildcard()
    {
        var r = Make();
        var e = r.Add(new RedirectEntry { From = "*:5000", To = "127.0.0.1:6000", Proto = "udp" });
        Assert.True(!r.Resolve("10.0.0.9", 5000, "tcp").Redirected, "udp-only entry ignores tcp");
        Assert.True(r.Resolve("10.0.0.9", 5000, "udp").Redirected, "wildcard host, udp");
        e.Enabled = false;
        r.Update(e.Id, e);
        Assert.True(!r.Resolve("10.0.0.9", 5000, "udp").Redirected, "switched off");
    }

    [Test]
    public static void Redirect_to_a_simulator_uses_its_port_or_falls_through()
    {
        var r = Make();
        r.SimResolver = id => id == "abc" ? ("127.0.0.1", 45678) : null;
        r.Add(new RedirectEntry { From = "192.168.1.50:23", To = "sim:abc" });
        var go = r.Resolve("192.168.1.50", 23);
        Assert.True(go.Redirected && go.Port == 45678, "goes to the simulator's port");
        r.Add(new RedirectEntry { From = "192.168.1.60:23", To = "sim:gone" });
        Assert.True(!r.Resolve("192.168.1.60", 23).Redirected, "a simulator that isn't listening: connect normally");
    }

    [Test]
    public static void Redirects_are_checked_and_saved()
    {
        var file = Path.Combine(Paths.TempData(), "redirects.json");
        var r = new NetworkRedirects(file);
        Assert.Throws<ArgumentException>(() => r.Add(new RedirectEntry { From = "", To = "127.0.0.1:1" }));
        Assert.Throws<ArgumentException>(() => r.Add(new RedirectEntry { From = "a:99999", To = "127.0.0.1:1" }));
        Assert.Throws<ArgumentException>(() => r.Add(new RedirectEntry { From = "a", To = "nowhere" }));
        Assert.Throws<ArgumentException>(() => r.Add(new RedirectEntry { From = "a", To = "b:1", Proto = "icmp" }));
        var e = r.Add(new RedirectEntry { From = "10.1.1.1:23", To = "sim:x", Note = "projector" });
        var again = new NetworkRedirects(file);
        Assert.Equal(1, again.List.Count, "reloaded");
        Assert.Equal("sim:x", again.List[0].To, "target kept");
        Assert.True(again.Remove(e.Id) && !again.Remove(e.Id), "remove once");
    }

    // ------------------------------------------------------------------ IP ID remap

    [Test]
    public static void Remap_sends_the_public_id_to_the_program_id_and_hides_the_old_one()
    {
        var m = new IpIdRemap(Path.Combine(Paths.TempData(), "ipremap.json"));
        m.Set(0x03, 0x0A);
        Assert.Equal(0x0Au, m.PublicOf(3), "program 03 answers at 0A");
        Assert.Equal(3u, m.ToProgram(0x0A)!.Value, "panel at 0A drives 03");
        Assert.True(m.ToProgram(0x03) == null, "03 no longer answers");
        Assert.Equal(7u, m.ToProgram(7)!.Value, "others pass through");
        m.Set(0x05, 0x03); // a program id can take over a number that was moved away
        Assert.Equal(5u, m.ToProgram(3)!.Value, "03 now reaches program id 05");
        Assert.Throws<ArgumentException>(() => m.Set(0x06, 0x0A));
        Assert.Throws<ArgumentException>(() => m.Set(0x00, 0x0A));
        Assert.Throws<ArgumentException>(() => m.Set(0x01, 0xFF));
        m.Set(0x03, 0x03);
        Assert.True(!m.IsRemapped(3), "mapping to itself removes it");
    }

    [Test]
    public static void Remap_refuses_a_public_id_the_program_uses_itself()
    {
        var m = new IpIdRemap(Path.Combine(Paths.TempData(), "ipremap.json"));
        Assert.Throws<ArgumentException>(() => m.Set(0x03, 0x04, id => id == 0x04));
        m.Set(0x03, 0x04, id => false);
    }

    [Test]
    public static void Remap_is_saved()
    {
        var f = Path.Combine(Paths.TempData(), "ipremap.json");
        new IpIdRemap(f).Set(0x21, 0x2F);
        Assert.Equal(0x2Fu, new IpIdRemap(f).PublicOf(0x21), "reloaded");
    }

    [Test]
    public static void A_panel_connecting_to_the_remapped_id_drives_the_programs_original_id()
    {
        using var p = new Processor(Paths.TempData(), 2);
        var slot = p.Slot(1);
        var dev = slot.RegisterDevice(0x03, "Panel", "test");
        var presses = new List<string>();
        dev.InputReceived += (_, k, v) => presses.Add($"{k.Kind}{k.Join}={v}");
        slot.SetIpRemap(0x03, 0x0A);

        var viaNew = DeviceDirectory.Resolve(p, 0x0A, 1) ?? throw new AssertException("resolved");
        Assert.True(ReferenceEquals(viaNew, dev), "the panel at 0A is the program's device 03");
        viaNew.ReceiveInput(new JoinKey(SigKind.Digital, 1), true);
        Assert.Equal("Digital1=True", string.Join(",", presses), "the program saw its own device get the press");
        dev.SetFeedback(new JoinKey(SigKind.Serial, 2), "hi");
        Assert.Equal("hi", (string)viaNew.GetFeedback(new JoinKey(SigKind.Serial, 2)), "feedback goes back to the same device");
        Assert.Equal(3u, viaNew.IpId, "the program still sees IP ID 03");

        // Without a slot (CIP): the slot that remaps the number gets it.
        Assert.True(ReferenceEquals(DeviceDirectory.Resolve(p, 0x0A, null), dev), "no slot given: still found");
        Assert.True(p.FindPanelDevice(0x0A) == dev && p.FindDevice(0x03) == dev, "lookups by public id and program id");

        // The old number no longer reaches it.
        var stray = DeviceDirectory.Resolve(p, 0x03, 1)!;
        Assert.True(!ReferenceEquals(stray, dev) && slot.FindDevice(3) == dev, "03 is not the program's device any more, and the program's device is untouched");

        // A panel can connect before the program registers the id.
        slot.SetIpRemap(0x31, 0x3A);
        var early = DeviceDirectory.Resolve(p, 0x3A, null)!;
        Assert.Equal(0x31u, early.IpId, "waiting device is created under the program's id");
        Assert.True(ReferenceEquals(slot.RegisterDevice(0x31, "Panel", "late"), early), "and the program gets that same device");

        slot.ClearIpRemap(0x03);
        Assert.True(ReferenceEquals(p.FindPanelDevice(0x03), dev), "back to normal");
        Assert.True(slot.RunConsoleCommand("ipt").Contains("IPID 31 (at 3A)"), "ipt shows the remap");
    }

    // ------------------------------------------------------------------ socket table

    [Test]
    public static void Socket_table_counts_bytes_and_keeps_only_a_little_text_when_nobody_watches()
    {
        var t = new SocketTable();
        var s = t.Open("TCPClient", "client", "tcp", "", "192.168.1.50:23");
        s.Connected("10.0.0.2:50000");
        var big = new byte[1000];
        for (var i = 0; i < 100; i++) s.Sent(big, 0, big.Length);
        Assert.Equal(100_000L, s.BytesOut, "bytes are always counted");
        var held = s.Traffic.Since();
        Assert.True(held.Count <= 30 && held.All(e => e.Text.Length <= 256), "unwatched: few short entries (" + held.Count + ")");
        t.Watch();
        s.Sent(big, 0, 1000);
        Assert.True(s.Traffic.Since().Last().Text.Length == 1000, "watched: whole text kept");
        s.Received(new byte[] { 65, 66 }, 0, 2);
        Assert.Equal("AB", s.Traffic.Since().Last().Text, "rx text");
        s.Close("closed by remote");
        Assert.True(t.All.Single().Closed != null && t.All.Single().State == "closed by remote", "closed and listed");
        for (var i = 0; i < 250; i++) t.Open("UDPServer", "server", "udp").Close();
        Assert.True(t.All.Count <= 201, "closed sockets are trimmed: " + t.All.Count);
    }
}
