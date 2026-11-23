using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OpenVP.Panels;

/// <summary>
/// Advertises OpenVP on the local network over mDNS (Bonjour/Avahi) so phones and PCs can open <c>http://openvp.local:8080</c> or
/// find "OpenVP on HOST" in a service browser. It answers queries for <c>&lt;hostname&gt;.local</c>, <c>openvp.local</c> and the
/// <c>_http._tcp</c> service. It is a small responder written for this one job (no name-conflict probing: if another machine
/// also calls itself <c>openvp.local</c>, use the host name). If multicast isn't available (a container on a bridge network, a
/// sandbox, no network), <see cref="Start"/> says so and returns false; nothing else is affected.
/// </summary>
public sealed class MdnsResponder : IDisposable
{
    public static readonly IPAddress Group = IPAddress.Parse("224.0.0.251");
    public const int MdnsPort = 5353;
    public const string ServiceType = "_http._tcp.local";

    private readonly int _httpPort;
    private readonly Action<string> _log;
    private readonly int _listenPort;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;

    public MdnsResponder(int httpPort, string? alias = "openvp", string? hostName = null, Action<string>? log = null, int listenPort = MdnsPort)
    {
        _httpPort = httpPort;
        _log = log ?? (_ => { });
        _listenPort = listenPort;
        HostLabel = Sanitize(hostName ?? Environment.MachineName);
        Alias = string.IsNullOrWhiteSpace(alias) ? null : Sanitize(alias);
        InstanceName = $"OpenVP on {hostName ?? Environment.MachineName}";
        Addresses = LocalAddresses;
    }

    /// <summary><c>hostname</c> as a DNS label.</summary>
    public string HostLabel { get; }
    public string? Alias { get; }
    public string InstanceName { get; }
    public bool Running => _udp != null;
    /// <summary>The addresses it advertises. Replaceable for tests.</summary>
    public Func<IReadOnlyList<IPAddress>> Addresses { get; set; }

    /// <summary>The names it answers for, e.g. <c>mybox.local</c> and <c>openvp.local</c>.</summary>
    public IReadOnlyList<string> HostNames =>
        new[] { HostLabel + ".local", Alias == null || Alias == HostLabel ? null : Alias + ".local" }.Where(x => x != null).Select(x => x!).ToList();

    private string InstanceFullName => $"{InstanceName}.{ServiceType}";

    public static string Sanitize(string s)
    {
        var chars = s.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-').ToArray();
        var t = new string(chars).Trim('-');
        return t.Length == 0 ? "openvp" : t.Length > 63 ? t[..63] : t;
    }

    public static IReadOnlyList<IPAddress> LocalAddresses() => Interfaces().Select(x => x.Ip).ToList();

    private static IEnumerable<(IPAddress Ip, NetworkInterface Nic)> Interfaces()
    {
        NetworkInterface[] nics;
        try { nics = NetworkInterface.GetAllNetworkInterfaces(); } catch { yield break; }
        foreach (var nic in nics)
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || !nic.SupportsMulticast) continue;
            IPInterfaceProperties props;
            try { props = nic.GetIPProperties(); } catch { continue; }
            foreach (var u in props.UnicastAddresses)
                if (u.Address.AddressFamily == AddressFamily.InterNetwork && !u.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                    yield return (u.Address, nic);
        }
    }

    /// <summary>Everything we know, as records (used for announcements and as the "additional" part of an answer).</summary>
    public List<DnsRecord> AllRecords(uint ttl = 120)
    {
        var recs = new List<DnsRecord>
        {
            DnsRecord.PtrRecord("_services._dns-sd._udp.local", ServiceType),
            DnsRecord.PtrRecord(ServiceType, InstanceFullName),
            DnsRecord.SrvRecord(InstanceFullName, HostLabel + ".local", _httpPort, ttl),
            DnsRecord.TxtRecord(InstanceFullName, new[] { "path=/", "product=OpenVP" }),
        };
        foreach (var name in HostNames)
            foreach (var ip in Addresses()) recs.Add(DnsRecord.ARecord(name, ip, ttl));
        return recs;
    }

    /// <summary>The response to a query, or null if nothing we know answers it.</summary>
    public DnsPacket? Answer(DnsPacket query)
    {
        if (query.IsResponse || query.Questions.Count == 0) return null;
        var all = AllRecords();
        var answers = new List<DnsRecord>();
        foreach (var q in query.Questions)
            foreach (var r in all)
                if (string.Equals(r.Name, q.Name, StringComparison.OrdinalIgnoreCase) && (q.Type == DnsRecord.Any || q.Type == r.Type) && !answers.Contains(r))
                    answers.Add(r);
        if (answers.Count == 0) return null;
        var resp = new DnsPacket { Id = query.Id, IsResponse = true };
        resp.Records.AddRange(answers);
        // What a browser needs next, so it doesn't have to ask again.
        if (answers.Any(a => a.Type == DnsRecord.Ptr && a.Name.Equals(ServiceType, StringComparison.OrdinalIgnoreCase)))
            foreach (var r in all)
                if (!answers.Contains(r) && (r.Type is DnsRecord.Srv or DnsRecord.Txt or DnsRecord.A)) resp.Records.Add(r);
        return resp;
    }

    // ---------------------------------------------------------------- running

    public bool Start()
    {
        if (_udp != null) return true;
        try
        {
            var udp = new UdpClient(AddressFamily.InterNetwork) { ExclusiveAddressUse = false };
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, _listenPort));
            var joined = 0;
            foreach (var (ip, _) in Interfaces())
            {
                try { udp.JoinMulticastGroup(Group, ip); joined++; } catch (SocketException) { }
            }
            if (joined == 0)
            {
                try { udp.JoinMulticastGroup(Group); joined++; } catch (SocketException) { }
            }
            if (joined == 0) { udp.Dispose(); _log("mDNS is off: no network interface can join the multicast group. OpenVP is still reachable by IP address."); return false; }
            udp.MulticastLoopback = false;
            _udp = udp;
            _cts = new CancellationTokenSource();
            _ = Listen(udp, _cts.Token);
            Announce();
            _ = Task.Delay(1000).ContinueWith(_ => Announce());
            _log($"mDNS: advertising OpenVP as {string.Join(", ", HostNames)} and the service \"{InstanceName}\" (port {_httpPort}).");
            return true;
        }
        catch (Exception e)
        {
            _log($"mDNS is off: {e.Message}. OpenVP is still reachable by IP address.");
            _udp?.Dispose(); _udp = null;
            return false;
        }
    }

    private void Announce()
    {
        try
        {
            var u = _udp;
            if (u == null) return;
            var p = new DnsPacket { IsResponse = true };
            p.Records.AddRange(AllRecords());
            var b = p.ToBytes();
            u.Send(b, b.Length, new IPEndPoint(Group, _listenPort));
        }
        catch (Exception) { /* multicast went away: nothing to do */ }
    }

    private async Task Listen(UdpClient udp, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await udp.ReceiveAsync(ct); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { await Task.Delay(500, CancellationToken.None); continue; }
            try
            {
                var q = DnsPacket.Parse(r.Buffer);
                if (q == null) continue;
                var resp = Answer(q);
                if (resp == null) continue;
                var bytes = resp.ToBytes();
                var legacy = r.RemoteEndPoint.Port != _listenPort; // an ordinary DNS client: answer it directly
                if (legacy || q.Questions.Any(x => x.UnicastResponse)) { resp.Id = q.Id; bytes = resp.ToBytes(); await udp.SendAsync(bytes, bytes.Length, r.RemoteEndPoint); }
                else { resp.Id = 0; bytes = resp.ToBytes(); await udp.SendAsync(bytes, bytes.Length, new IPEndPoint(Group, _listenPort)); }
            }
            catch (Exception) { /* a bad packet or a send failure must not stop the responder */ }
        }
    }

    public void Dispose()
    {
        var u = _udp;
        _udp = null;
        if (u == null) return;
        try
        {
            // Goodbye: the same records with a time to live of 0, so caches forget us.
            var p = new DnsPacket { IsResponse = true };
            foreach (var r in AllRecords()) { r.Ttl = 0; p.Records.Add(r); }
            var b = p.ToBytes();
            u.Send(b, b.Length, new IPEndPoint(Group, _listenPort));
        }
        catch (Exception) { }
        _cts?.Cancel();
        try { u.Dispose(); } catch { }
    }
}
