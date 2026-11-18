using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using OpenVP.Core;

namespace OpenVP.Panels;

/// <summary>
/// Works out who is on the other end of a panel connection, without credentials:
///  - this computer (loopback) or reverse DNS for the hostname;
///  - Crestron autodiscovery (UDP 41794), which Crestron hardware answers with its hostname, model,
///    firmware and MAC. XPanel and the Crestron apps don't answer it, which is itself a useful hint;
///  - for browser panels, the browser and OS from the User-Agent and what the page reports about itself.
/// Everything runs in the background after a client attaches and never delays the connection.
///
/// EXPERIMENTAL: the discovery packet follows what community tools send; like CIP, it has not been
/// checked against every device. The raw reply is kept in the connection's details when it can't be parsed.
/// </summary>
public static partial class ClientIdentifier
{
    public const int DiscoveryPort = 41794;
    private static readonly ConcurrentDictionary<string, (DateTimeOffset at, string? name)> DnsCache = new();
    private static readonly TimeSpan DnsCacheFor = TimeSpan.FromMinutes(10);

    /// <summary>Turn off the network probes (DNS and discovery), e.g. in tests or locked-down networks.</summary>
    public static bool NetworkProbesEnabled { get; set; } = true;

    /// <summary>Start identifying in the background.</summary>
    public static void Begin(DeviceConnection c) => _ = IdentifyAsync(c);

    public static async Task IdentifyAsync(DeviceConnection c, CancellationToken ct = default)
    {
        c.ProbeStatus = "identifying…";
        try
        {
            var ip = IPAddress.TryParse(c.RemoteAddress, out var a) ? Normalize(a) : null;
            if (c.UserAgent != null) ApplyUserAgent(c, c.UserAgent);

            if (ip != null && IPAddress.IsLoopback(ip))
            {
                c.Hostname = Environment.MachineName;
                c.SetDetail("Location", "this computer (the OpenVP host)");
                c.ClientKind ??= c.Transport == "CIP" ? "XPanel or app on this computer" : c.ClientKind;
                c.ProbeStatus = "identified (same computer)";
                return;
            }
            if (ip == null || !NetworkProbesEnabled)
            {
                c.ProbeStatus = NetworkProbesEnabled ? "no address to probe" : "network probes off";
                return;
            }

            var dns = ReverseDnsAsync(ip, ct);
            var disco = DiscoverAsync(ip, ct);
            var name = await dns;
            if (name != null) { c.Hostname ??= name; c.SetDetail("Reverse DNS", name); }

            var reply = await disco;
            if (reply != null)
            {
                ApplyDiscovery(c, reply);
                c.ProbeStatus = "identified (Crestron autodiscovery)";
            }
            else
            {
                c.SetDetail("Autodiscovery", "no reply");
                if (c.Transport == "CIP")
                {
                    c.ClientKind ??= "Software client (XPanel or Crestron app?)";
                    c.ProbeStatus = "no discovery reply: usually XPanel or a Crestron app on a computer or phone (or autodiscovery is off on the device)";
                }
                else c.ProbeStatus = name != null ? "identified (reverse DNS)" : "no reverse DNS or discovery reply";
            }
        }
        catch (Exception e)
        {
            c.ProbeStatus = "identify failed: " + e.Message;
        }
        finally { c.LastProbe = DateTimeOffset.Now; }
    }

    private static IPAddress Normalize(IPAddress a) => a.IsIPv4MappedToIPv6 ? a.MapToIPv4() : a;

    // ------------------------------------------------------------------ DNS

    private static async Task<string?> ReverseDnsAsync(IPAddress ip, CancellationToken ct)
    {
        var key = ip.ToString();
        if (DnsCache.TryGetValue(key, out var hit) && DateTimeOffset.Now - hit.at < DnsCacheFor) return hit.name;
        string? name = null;
        try
        {
            var lookup = Dns.GetHostEntryAsync(ip);
            if (await Task.WhenAny(lookup, Task.Delay(3000, ct)) == lookup)
            {
                var host = lookup.Result.HostName;
                if (!string.IsNullOrWhiteSpace(host) && host != key) name = host;
            }
        }
        catch (Exception) { /* no PTR record: leave it blank */ }
        DnsCache[key] = (DateTimeOffset.Now, name);
        return name;
    }

    // ------------------------------------------------------------------ Crestron autodiscovery

    public sealed record DiscoveryReply(string? Hostname, string? Description, string? Model, string? Firmware, string? FirmwareDate, string? Mac, string RawHex);

    /// <summary>The query: type 0x14, then the asking host's name in a 256-byte field.</summary>
    public static byte[] DiscoveryQuery(string hostname)
    {
        var p = new byte[10 + 256];
        new byte[] { 0x14, 0x00, 0x00, 0x00, 0x01, 0x04, 0x00, 0x03, 0x00, 0x00 }.CopyTo(p, 0);
        var name = Encoding.ASCII.GetBytes(hostname);
        Array.Copy(name, 0, p, 10, Math.Min(name.Length, 255));
        return p;
    }

    /// <summary>Ask one address directly (unicast), twice, 1.5 s each.</summary>
    public static async Task<DiscoveryReply?> DiscoverAsync(IPAddress ip, CancellationToken ct = default, int port = DiscoveryPort)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return null; // Crestron discovery is IPv4
        try
        {
            using var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            var query = DiscoveryQuery(Environment.MachineName);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                await udp.SendAsync(query, query.Length, new IPEndPoint(ip, port));
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(1500);
                try
                {
                    while (true)
                    {
                        var r = await udp.ReceiveAsync(cts.Token);
                        if (!r.RemoteEndPoint.Address.Equals(ip)) continue;
                        if (ParseDiscovery(r.Buffer) is { } reply) return reply;
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            }
        }
        catch (SocketException) { }
        return null;
    }

    /// <summary>
    /// Parse a reply: type 0x15, hostname at offset 10, description ("TSW-760 [v3.002.1061 (Aug 05 2021), #7FB60A3F] @E-00107fd2e3c5")
    /// at offset 266. Falls back to picking printable strings out of the packet if the layout differs.
    /// </summary>
    public static DiscoveryReply? ParseDiscovery(byte[] data)
    {
        if (data.Length < 12 || data[0] == 0x14) return null; // too short, or someone else's query
        string? hostname = CString(data, 10);
        string? description = data.Length > 266 ? CString(data, 266) : null;
        if (string.IsNullOrEmpty(description))
        {
            var strings = PrintableRun().Matches(Encoding.Latin1.GetString(data)).Select(m => m.Value.Trim()).Where(s => s.Length >= 3).ToList();
            hostname = string.IsNullOrEmpty(hostname) ? strings.FirstOrDefault() : hostname;
            description = strings.FirstOrDefault(s => s.Contains("[v") || s.Contains("@E-")) ?? strings.Skip(1).FirstOrDefault();
        }
        if (string.IsNullOrEmpty(hostname) && string.IsNullOrEmpty(description)) return null;

        string? model = null, fw = null, fwDate = null, mac = null;
        if (description != null)
        {
            var m = DescriptionPattern().Match(description);
            if (m.Success)
            {
                model = m.Groups["model"].Value.Trim();
                fw = m.Groups["fw"].Success ? m.Groups["fw"].Value : null;
                fwDate = m.Groups["date"].Success ? m.Groups["date"].Value : null;
            }
            var mm = MacPattern().Match(description);
            if (mm.Success) mac = string.Join(":", Enumerable.Range(0, 6).Select(i => mm.Groups[1].Value.Substring(i * 2, 2))).ToUpperInvariant();
        }
        return new DiscoveryReply(NullIfEmpty(hostname), NullIfEmpty(description), NullIfEmpty(model), fw, fwDate, mac,
            Convert.ToHexString(data, 0, Math.Min(data.Length, 512)));
    }

    public static void ApplyDiscovery(DeviceConnection c, DiscoveryReply r)
    {
        if (r.Hostname != null) c.Hostname = r.Hostname;
        if (r.Model != null) c.Model = r.Model;
        if (r.Firmware != null) c.Firmware = r.Firmware;
        if (r.Mac != null) c.MacAddress = r.Mac;
        c.SetDetail("Autodiscovery", r.Description ?? "(reply without a description)");
        c.SetDetail("Firmware date", r.FirmwareDate);
        if (r.Model == null) c.SetDetail("Autodiscovery raw", r.RawHex);
        c.ClientKind = DescribeModel(r.Model, c.Transport);
    }

    /// <summary>"TSW-1070" → "Crestron touch panel (TSW-1070)".</summary>
    public static string DescribeModel(string? model, string transport)
    {
        if (string.IsNullOrEmpty(model)) return "Crestron device";
        var m = model.ToUpperInvariant();
        var family =
            m.StartsWith("TSW") || m.StartsWith("TS-") || m.StartsWith("TSS") || m.StartsWith("TST") || m.StartsWith("TSR") || m.StartsWith("TPMC") || m.StartsWith("TSD") ? "touch panel" :
            m.StartsWith("CP") || m.StartsWith("MC") || m.StartsWith("PRO") || m.StartsWith("RMC") || m.StartsWith("AV") || m.StartsWith("VC-4") ? "control processor" :
            m.StartsWith("DM") || m.StartsWith("HD-") || m.StartsWith("NVX") ? "DigitalMedia / AV device" :
            m.StartsWith("UC-") || m.StartsWith("MERCURY") || m.StartsWith("FLEX") ? "UC / conferencing device" :
            m.StartsWith("DIN") || m.StartsWith("CLW") || m.StartsWith("C2N") || m.StartsWith("CEN") ? "control module" :
            "device";
        var via = transport == "Web panel" ? ", running a browser/CH5 project" : "";
        return $"Crestron {family} ({model}){via}";
    }

    // ------------------------------------------------------------------ browsers

    /// <summary>Browser, OS and device class from a User-Agent; good enough to tell a phone from a TSW from a PC.</summary>
    public static void ApplyUserAgent(DeviceConnection c, string ua)
    {
        c.UserAgent = ua;
        var browser =
            Match(ua, @"Edg/(\d+)") is { } edge ? $"Edge {edge}" :
            Match(ua, @"OPR/(\d+)") is { } opera ? $"Opera {opera}" :
            Match(ua, @"Firefox/(\d+)") is { } ff ? $"Firefox {ff}" :
            Match(ua, @"Chrome/(\d+)") is { } chrome ? $"Chrome {chrome}" :
            Match(ua, @"Version/(\d+)[\d.]* .*Safari") is { } safari ? $"Safari {safari}" :
            ua.Contains("Electron") ? "Electron" : null;
        var os =
            ua.Contains("Windows NT 10") ? "Windows 10/11" :
            ua.Contains("Windows") ? "Windows" :
            ua.Contains("iPad") ? "iPadOS" :
            ua.Contains("iPhone") ? "iOS" :
            ua.Contains("Android") ? "Android" :
            ua.Contains("Mac OS X") ? "macOS" :
            ua.Contains("CrOS") ? "ChromeOS" :
            ua.Contains("Linux") ? "Linux" : null;
        var crestron = ua.Contains("Crestron", StringComparison.OrdinalIgnoreCase);
        c.SetDetail("Browser", browser);
        c.SetDetail("OS", os);
        if (c.ClientKind == null || c.ClientKind.StartsWith("Browser"))
            c.ClientKind = crestron ? "CH5 project on a Crestron panel"
                : $"Browser panel{(browser != null || os != null ? $" ({string.Join(" on ", new[] { browser, os }.Where(x => x != null))})" : "")}";
    }

    private static string? Match(string s, string pattern)
    {
        var m = Regex.Match(s, pattern);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static string? CString(byte[] data, int offset)
    {
        if (offset >= data.Length) return null;
        var end = Array.IndexOf(data, (byte)0, offset);
        if (end < 0) end = data.Length;
        var s = Encoding.Latin1.GetString(data, offset, end - offset).Trim();
        return s.All(ch => ch >= 0x20 && ch < 0x7F) ? s : null;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    [GeneratedRegex(@"[\x20-\x7E]{3,}")]
    private static partial Regex PrintableRun();

    [GeneratedRegex(@"^(?<model>[^\[]+?)\s*\[\s*v?(?<fw>[0-9][^\s,\]\(]*)?\s*(?:\((?<date>[^)]*)\))?")]
    private static partial Regex DescriptionPattern();

    [GeneratedRegex(@"@E-([0-9A-Fa-f]{12})")]
    private static partial Regex MacPattern();
}
