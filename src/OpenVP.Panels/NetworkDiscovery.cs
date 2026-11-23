using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace OpenVP.Panels;

/// <summary>A device found on the network.</summary>
public sealed class DiscoveredDevice
{
    public string Ip { get; set; } = "";
    public string? Hostname { get; set; }
    public string? Mac { get; set; }
    public string? Model { get; set; }
    public string? Firmware { get; set; }
    public string? Description { get; set; }
    public List<int> OpenPorts { get; set; } = new();
    /// <summary>How it was found: <c>crestron</c>, <c>mdns</c>, <c>ping</c>, <c>arp</c>, <c>tcp</c>.</summary>
    public List<string> Sources { get; set; } = new();
    /// <summary>mDNS services it advertises, e.g. <c>_http._tcp :80</c>.</summary>
    public List<string> Services { get; set; } = new();
}

public sealed class DiscoveryReport
{
    public bool Running { get; set; }
    public string Stage { get; set; } = "idle";
    public DateTimeOffset? Started { get; set; }
    public double Seconds { get; set; }
    public List<string> Subnets { get; set; } = new();
    public List<DiscoveredDevice> Devices { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

/// <summary>Parsers for the things discovery reads. Kept separate so they are tested without a network.</summary>
public static class DiscoveryParsing
{
    private static readonly Regex ArpLinux = new(@"^(?<ip>\d{1,3}(?:\.\d{1,3}){3})\s+\S+\s+(?<flags>0x[0-9a-f]+)\s+(?<mac>[0-9a-f]{2}(?::[0-9a-f]{2}){5})", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex ArpWindows = new(@"^\s*(?<ip>\d{1,3}(?:\.\d{1,3}){3})\s+(?<mac>[0-9a-f]{2}(?:-[0-9a-f]{2}){5})\s+\w+", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex ArpUnix = new(@"\((?<ip>\d{1,3}(?:\.\d{1,3}){3})\)\s+at\s+(?<mac>[0-9a-f]{1,2}(?::[0-9a-f]{1,2}){5})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Entries of an ARP table: <c>/proc/net/arp</c> on Linux, or the output of <c>arp -a</c> on Windows, macOS and Linux.
    /// Incomplete entries (all-zero addresses) are left out.</summary>
    public static List<(string Ip, string Mac)> ParseArp(string text)
    {
        var list = new List<(string, string)>();
        void Add(string ip, string mac)
        {
            var m = string.Join(":", mac.Split('-', ':').Select(x => x.PadLeft(2, '0'))).ToUpperInvariant();
            if (m == "00:00:00:00:00:00" || m == "FF:FF:FF:FF:FF:FF" || list.Any(x => x.Item1 == ip)) return;
            list.Add((ip, m));
        }
        foreach (Match m in ArpLinux.Matches(text))
            if (int.TryParse(m.Groups["flags"].Value[2..], NumberStyles.HexNumber, null, out var f) && (f & 2) != 0) Add(m.Groups["ip"].Value, m.Groups["mac"].Value);
        foreach (Match m in ArpWindows.Matches(text)) Add(m.Groups["ip"].Value, m.Groups["mac"].Value);
        foreach (Match m in ArpUnix.Matches(text)) Add(m.Groups["ip"].Value, m.Groups["mac"].Value);
        return list;
    }

    /// <summary>Fold mDNS records from a response into per-address facts: host name, services.</summary>
    public static IEnumerable<(string Ip, string Host, List<string> Services)> FromMdns(IEnumerable<DnsRecord> records)
    {
        var recs = records.ToList();
        var addr = recs.Where(r => r.Type == DnsRecord.A && r.Address != null).GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Address!.ToString(), StringComparer.OrdinalIgnoreCase);
        var srv = recs.Where(r => r.Type == DnsRecord.Srv).ToList();
        var found = new Dictionary<string, (string Host, List<string> Services)>();
        foreach (var s in srv)
        {
            if (s.Target == null || !addr.TryGetValue(s.Target, out var ip)) continue;
            var dot = s.Name.IndexOf("._", StringComparison.Ordinal);
            var type = dot >= 0 ? s.Name[(dot + 1)..].Replace(".local", "") : s.Name;
            if (!found.TryGetValue(ip, out var f)) found[ip] = f = (s.Target.Replace(".local", ""), new List<string>());
            var label = $"{type} :{s.Port}";
            if (!f.Services.Contains(label)) f.Services.Add(label);
        }
        foreach (var (name, ip) in addr)
            if (!found.ContainsKey(ip)) found[ip] = (name.Replace(".local", ""), new List<string>());
        return found.Select(kv => (kv.Key, kv.Value.Host, kv.Value.Services));
    }

    /// <summary>Fold one more finding into the list, by address.</summary>
    public static DiscoveredDevice Touch(List<DiscoveredDevice> all, string ip, string source)
    {
        var d = all.FirstOrDefault(x => x.Ip == ip);
        if (d == null) { d = new DiscoveredDevice { Ip = ip }; all.Add(d); }
        if (!d.Sources.Contains(source)) d.Sources.Add(source);
        return d;
    }

    public static void ApplyCrestron(DiscoveredDevice d, ClientIdentifier.DiscoveryReply r)
    {
        d.Hostname ??= r.Hostname;
        d.Model = r.Model ?? d.Model;
        d.Firmware = r.Firmware ?? d.Firmware;
        d.Mac = r.Mac ?? d.Mac;
        d.Description = r.Description ?? d.Description;
    }

    public static int CompareIp(string a, string b)
    {
        static uint N(string s) => IPAddress.TryParse(s, out var ip) && ip.GetAddressBytes() is { Length: 4 } x ? (uint)(x[0] << 24 | x[1] << 16 | x[2] << 8 | x[3]) : 0;
        return N(a).CompareTo(N(b));
    }
}

public sealed record DiscoverySubnet(IPAddress Local, IPAddress Mask, string Nic)
{
    public int Prefix => Mask.GetAddressBytes().Sum(b => Convert.ToString(b, 2).Count(c => c == '1'));
    public IPAddress Broadcast
    {
        get
        {
            var a = Local.GetAddressBytes(); var m = Mask.GetAddressBytes();
            return new IPAddress(a.Select((x, i) => (byte)(x | ~m[i])).ToArray());
        }
    }

    /// <summary>The hosts to look at: the subnet, but never more than the /24 around this computer's address.</summary>
    public IEnumerable<IPAddress> Hosts()
    {
        var a = Local.GetAddressBytes();
        var m = Mask.GetAddressBytes();
        var net = a.Select((x, i) => (byte)(x & m[i])).ToArray();
        var count = Math.Min(254, (int)Math.Min(1L << Math.Max(0, 32 - Prefix), 256) - 2);
        if (Prefix >= 24)
            for (var i = 1; i <= count; i++) { var h = (byte[])net.Clone(); h[3] = (byte)(net[3] + i); yield return new IPAddress(h); }
        else
            for (var i = 1; i <= 254; i++) { var h = new[] { a[0], a[1], a[2], (byte)i }; yield return new IPAddress(h); }
    }
}

/// <summary>
/// "Discover" on the console. It looks for devices on this computer's own subnets, only when asked, and gently:
/// Crestron autodiscovery (a UDP broadcast on 41794), an mDNS browse for common AV services, and then a quick TCP connect to a few
/// control ports on the hosts that answered ping or are in the ARP table. At most 48 connections are in flight, each gives up
/// after half a second, and the whole scan ends after 30 seconds whatever happens.
/// </summary>
public sealed class NetworkDiscovery
{
    public static readonly int[] DefaultProbePorts = { 23, 80, 443, 41794, 41795, 1515, 4352 };
    public static readonly string[] MdnsServices =
    {
        "_http._tcp.local", "_https._tcp.local", "_airplay._tcp.local", "_raop._tcp.local", "_googlecast._tcp.local", "_ipp._tcp.local",
        "_ssh._tcp.local", "_telnet._tcp.local", "_pjlink._tcp.local", "_ndi._tcp.local", "_netaudio-arc._udp.local", "_netaudio-cmc._udp.local",
        "_workstation._tcp.local", "_device-info._tcp.local", "_smb._tcp.local",
    };

    private readonly object _gate = new();
    private DiscoveryReport _report = new();
    private CancellationTokenSource? _cts;

    public int[] ProbePorts { get; set; } = DefaultProbePorts;
    public int MaxConcurrency { get; set; } = 48;
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan Overall { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ListenFor { get; set; } = TimeSpan.FromMilliseconds(2500);
    /// <summary>Replace the subnets (tests). Null uses this computer's.</summary>
    public Func<IReadOnlyList<DiscoverySubnet>>? SubnetsOverride { get; set; }
    /// <summary>Replace the hosts that are looked at (tests); when set, broadcast, mDNS and ping steps are skipped.</summary>
    public Func<IReadOnlyList<IPAddress>>? HostsOverride { get; set; }

    public DiscoveryReport Snapshot()
    {
        lock (_gate)
        {
            var r = new DiscoveryReport
            {
                Running = _report.Running, Stage = _report.Stage, Started = _report.Started, Subnets = _report.Subnets.ToList(), Notes = _report.Notes.ToList(),
                Seconds = _report.Started is { } s && _report.Running ? (DateTimeOffset.Now - s).TotalSeconds : _report.Seconds,
                Devices = _report.Devices.Select(Copy).OrderBy(d => d.Ip, Comparer<string>.Create(DiscoveryParsing.CompareIp)).ToList(),
            };
            return r;
        }
    }

    private static DiscoveredDevice Copy(DiscoveredDevice d) => new()
    {
        Ip = d.Ip, Hostname = d.Hostname, Mac = d.Mac, Model = d.Model, Firmware = d.Firmware, Description = d.Description,
        OpenPorts = d.OpenPorts.OrderBy(x => x).ToList(), Sources = d.Sources.ToList(), Services = d.Services.ToList()
    };

    public bool Running { get { lock (_gate) return _report.Running; } }

    /// <summary>Start a scan in the background. False if one is already running.</summary>
    public bool Start()
    {
        lock (_gate)
        {
            if (_report.Running) return false;
            _report = new DiscoveryReport { Running = true, Stage = "starting", Started = DateTimeOffset.Now };
            _cts = new CancellationTokenSource(Overall);
        }
        var ct = _cts!.Token;
        _ = Task.Run(async () =>
        {
            try { await RunAsync(ct); }
            catch (OperationCanceledException) { Note("The scan hit its time limit and stopped; what it found is listed."); }
            catch (Exception e) { Note($"The scan stopped: {e.Message}"); }
            finally
            {
                lock (_gate) { _report.Running = false; _report.Stage = "done"; _report.Seconds = (DateTimeOffset.Now - _report.Started!.Value).TotalSeconds; }
            }
        });
        return true;
    }

    public void Cancel() { lock (_gate) _cts?.Cancel(); }

    private void Note(string text) { lock (_gate) if (!_report.Notes.Contains(text)) _report.Notes.Add(text); }
    private void Stage(string s) { lock (_gate) _report.Stage = s; }
    private void With(Action<List<DiscoveredDevice>> act) { lock (_gate) act(_report.Devices); }

    public static IReadOnlyList<DiscoverySubnet> LocalSubnets()
    {
        var list = new List<DiscoverySubnet>();
        NetworkInterface[] nics;
        try { nics = NetworkInterface.GetAllNetworkInterfaces(); } catch { return list; }
        foreach (var nic in nics)
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            IPInterfaceProperties props;
            try { props = nic.GetIPProperties(); } catch { continue; }
            foreach (var u in props.UnicastAddresses)
            {
                if (u.Address.AddressFamily != AddressFamily.InterNetwork || u.IPv4Mask == null) continue;
                if (u.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal)) continue;
                list.Add(new DiscoverySubnet(u.Address, u.IPv4Mask, nic.Name));
            }
        }
        return list;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        IReadOnlyList<IPAddress> hosts;
        IReadOnlyList<DiscoverySubnet> subnets = SubnetsOverride?.Invoke() ?? LocalSubnets();
        lock (_gate) _report.Subnets = subnets.Select(s => $"{s.Local}/{s.Prefix} ({s.Nic})").ToList();
        if (HostsOverride != null) hosts = HostsOverride();
        else
        {
            if (subnets.Count == 0) { Note("No network address was found on this computer, so there is nothing to scan."); return; }
            hosts = subnets.SelectMany(s => s.Hosts()).Where(h => !subnets.Any(s => s.Local.Equals(h))).Distinct().Take(512).ToList();

            Stage("asking Crestron devices, mDNS and pinging");
            var alive = new List<IPAddress>();
            await Task.WhenAll(
                Guard(() => CrestronBroadcast(subnets, ct), "Crestron autodiscovery"),
                Guard(() => MdnsBrowse(subnets, ct), "mDNS"),
                Guard(async () => alive.AddRange(await PingSweep(hosts, ct)), "ping"));
            foreach (var ip in alive) With(l => DiscoveryParsing.Touch(l, ip.ToString(), "ping"));
            await Guard(() => { ReadArp(subnets); return Task.CompletedTask; }, "ARP table");
        }

        // This computer answers its own mDNS and shows up in the ARP table: leave it out of the list.
        With(l => l.RemoveAll(d => subnets.Any(s => s.Local.ToString() == d.Ip)));

        Stage("checking control ports");
        List<IPAddress> candidates;
        if (HostsOverride != null) candidates = hosts.ToList();
        else
        {
            lock (_gate) candidates = _report.Devices.Select(d => IPAddress.Parse(d.Ip)).ToList();
        }
        await ProbePortsAsync(candidates, ct);

        Stage("looking up names");
        await Names(ct);
    }

    private async Task Guard(Func<Task> work, string what)
    {
        try { await work(); }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) { Note($"{what} wasn't available: {e.Message}"); }
    }

    // ---------------------------------------------------------------- Crestron autodiscovery

    private async Task CrestronBroadcast(IReadOnlyList<DiscoverySubnet> subnets, CancellationToken ct)
    {
        var query = ClientIdentifier.DiscoveryQuery(Environment.MachineName);
        var clients = new List<UdpClient>();
        try
        {
            foreach (var s in subnets)
            {
                try
                {
                    var u = new UdpClient(new IPEndPoint(s.Local, 0)) { EnableBroadcast = true };
                    clients.Add(u);
                    await u.SendAsync(query, query.Length, new IPEndPoint(s.Broadcast, ClientIdentifier.DiscoveryPort));
                }
                catch (SocketException) { }
            }
            if (clients.Count == 0) { Note("Crestron autodiscovery: no interface could send the broadcast."); return; }
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stop.CancelAfter(ListenFor);
            await Task.WhenAll(clients.Select(async u =>
            {
                try
                {
                    while (true)
                    {
                        var r = await u.ReceiveAsync(stop.Token);
                        if (ClientIdentifier.ParseDiscovery(r.Buffer) is not { } reply) continue;
                        var ip = r.RemoteEndPoint.Address.ToString();
                        With(l => DiscoveryParsing.ApplyCrestron(DiscoveryParsing.Touch(l, ip, "crestron"), reply));
                    }
                }
                catch (OperationCanceledException) { }
                catch (SocketException) { }
            }));
        }
        finally { foreach (var u in clients) u.Dispose(); }
    }

    // ---------------------------------------------------------------- mDNS

    private async Task MdnsBrowse(IReadOnlyList<DiscoverySubnet> subnets, CancellationToken ct)
    {
        var query = DnsPacket.Query(MdnsServices, DnsRecord.Ptr, unicastResponse: true);
        var records = new List<DnsRecord>();
        var clients = new List<UdpClient>();
        try
        {
            foreach (var s in subnets)
            {
                try
                {
                    var u = new UdpClient(new IPEndPoint(s.Local, 0));
                    u.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, s.Local.GetAddressBytes());
                    u.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
                    clients.Add(u);
                    await u.SendAsync(query, query.Length, new IPEndPoint(MdnsResponder.Group, MdnsResponder.MdnsPort));
                }
                catch (SocketException) { }
            }
            if (clients.Count == 0) { Note("mDNS: multicast isn't available on this network."); return; }
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stop.CancelAfter(ListenFor);
            await Task.WhenAll(clients.Select(async u =>
            {
                try
                {
                    while (true)
                    {
                        var r = await u.ReceiveAsync(stop.Token);
                        if (DnsPacket.Parse(r.Buffer) is { IsResponse: true } p) lock (records) records.AddRange(p.Records);
                    }
                }
                catch (OperationCanceledException) { }
                catch (SocketException) { }
            }));
        }
        finally { foreach (var u in clients) u.Dispose(); }
        List<DnsRecord> snapshot;
        lock (records) snapshot = records.ToList();
        foreach (var (ip, host, services) in DiscoveryParsing.FromMdns(snapshot))
            With(l =>
            {
                var d = DiscoveryParsing.Touch(l, ip, "mdns");
                d.Hostname ??= host;
                foreach (var s in services) if (!d.Services.Contains(s)) d.Services.Add(s);
            });
    }

    // ---------------------------------------------------------------- ping and ARP

    private async Task<List<IPAddress>> PingSweep(IReadOnlyList<IPAddress> hosts, CancellationToken ct)
    {
        var alive = new List<IPAddress>();
        using var gate = new SemaphoreSlim(40);
        var failed = 0;
        await Task.WhenAll(hosts.Select(async h =>
        {
            await gate.WaitAsync(ct);
            try
            {
                using var ping = new Ping();
                var r = await ping.SendPingAsync(h, 400);
                if (r.Status == IPStatus.Success) lock (alive) alive.Add(h);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { if (Interlocked.Increment(ref failed) == 1) Note("Ping isn't available here (it may need permission); looking at the ARP table and ports instead."); }
            finally { gate.Release(); }
        }));
        return alive;
    }

    private void ReadArp(IReadOnlyList<DiscoverySubnet> subnets)
    {
        string text = "";
        try
        {
            if (File.Exists("/proc/net/arp")) text = File.ReadAllText("/proc/net/arp");
            else
            {
                using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("arp", "-a") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true });
                if (p != null) { text = p.StandardOutput.ReadToEnd(); if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } } }
            }
        }
        catch (Exception) { Note("The ARP table couldn't be read."); return; }
        foreach (var (ip, mac) in DiscoveryParsing.ParseArp(text))
        {
            if (!IPAddress.TryParse(ip, out var a) || !subnets.Any(s => InSubnet(a, s))) continue;
            With(l => { var d = DiscoveryParsing.Touch(l, ip, "arp"); d.Mac ??= mac; });
        }
    }

    private static bool InSubnet(IPAddress a, DiscoverySubnet s)
    {
        var x = a.GetAddressBytes(); var l = s.Local.GetAddressBytes(); var m = s.Mask.GetAddressBytes();
        return x.Select((b, i) => b & m[i]).SequenceEqual(l.Select((b, i) => b & m[i]));
    }

    // ---------------------------------------------------------------- ports and names

    private async Task ProbePortsAsync(IReadOnlyList<IPAddress> hosts, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(MaxConcurrency);
        var jobs = hosts.SelectMany(h => ProbePorts.Select(p => (h, p))).ToList();
        await Task.WhenAll(jobs.Select(async j =>
        {
            await gate.WaitAsync(ct);
            try
            {
                using var c = new TcpClient();
                using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                t.CancelAfter(ConnectTimeout);
                try { await c.ConnectAsync(j.h, j.p, t.Token); }
                catch (Exception) { return; }
                var ip = j.h.ToString();
                With(l => { var d = DiscoveryParsing.Touch(l, ip, "tcp"); if (!d.OpenPorts.Contains(j.p)) d.OpenPorts.Add(j.p); });
            }
            finally { gate.Release(); }
        }));
    }

    private async Task Names(CancellationToken ct)
    {
        List<DiscoveredDevice> need;
        lock (_gate) need = _report.Devices.Where(d => string.IsNullOrEmpty(d.Hostname)).ToList();
        using var gate = new SemaphoreSlim(16);
        await Task.WhenAll(need.Select(async d =>
        {
            await gate.WaitAsync(ct);
            try
            {
                using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                t.CancelAfter(1000);
                var e = await Dns.GetHostEntryAsync(d.Ip, t.Token);
                if (!string.IsNullOrEmpty(e.HostName) && e.HostName != d.Ip) lock (_gate) d.Hostname ??= e.HostName;
            }
            catch (Exception) { }
            finally { gate.Release(); }
        }));
    }
}
