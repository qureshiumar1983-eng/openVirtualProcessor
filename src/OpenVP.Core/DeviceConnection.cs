using System.Collections.Concurrent;

namespace OpenVP.Core;

/// <summary>
/// One client attached to an IP table entry: a touch panel or XPanel over CIP, a browser panel, or an
/// EISC peer. Holds who the client is (address, hostname, what it identified as) and live counters, so
/// the IP table can show what each IP ID is connected to, not just whether it is online.
/// Counters are updated from transport threads without locks; readers get a consistent-enough view.
/// </summary>
public sealed class DeviceConnection
{
    private static long _nextId;
    private readonly ConcurrentDictionary<string, string> _details = new(StringComparer.OrdinalIgnoreCase);
    private long _lastActivityTicks;

    public DeviceConnection(string transport, string? remoteAddress = null, int remotePort = 0)
    {
        Id = Interlocked.Increment(ref _nextId).ToString();
        Transport = transport;
        RemoteAddress = remoteAddress ?? "";
        RemotePort = remotePort;
        ConnectedAt = DateTimeOffset.Now;
        _lastActivityTicks = ConnectedAt.UtcTicks;
    }

    /// <summary>Unique for the life of the process; the REST API addresses a connection by it.</summary>
    public string Id { get; }

    /// <summary>"CIP", "Web panel", "EISC" …</summary>
    public string Transport { get; }
    public string RemoteAddress { get; }
    public int RemotePort { get; }
    public DateTimeOffset ConnectedAt { get; }

    /// <summary>Best guess at what the client is, for the first column of the IP table ("TSW-1070", "XPanel or app on a computer").</summary>
    public string? ClientKind { get; set; }
    /// <summary>Reverse-DNS name, a discovery hostname, or the name the device reported when logged into.</summary>
    public string? Hostname { get; set; }
    public string? Model { get; set; }
    public string? Firmware { get; set; }
    public string? MacAddress { get; set; }
    public string? UserAgent { get; set; }
    /// <summary>For EISC: the slot/program on the other end.</summary>
    public string? Peer { get; set; }
    /// <summary>What the identification probes are doing: "identifying…", "identified", "no discovery reply" …</summary>
    public string? ProbeStatus { get; set; }
    public DateTimeOffset? LastProbe { get; set; }

    /// <summary>Packets waiting to be sent to this client (a transport fills it in), or null.</summary>
    public Func<int>? QueueDepth { get; set; }

    // ---------- counters ----------
    public long MessagesIn;
    public long MessagesOut;
    public long BytesIn;
    public long BytesOut;
    /// <summary>Join changes received from the client (presses, slider moves, text).</summary>
    public long JoinsIn;
    /// <summary>Feedback join changes sent to the client.</summary>
    public long JoinsOut;
    public long Heartbeats;
    public long Errors;
    /// <summary>Round-trip time the client last reported or that was measured, in ms (-1 = unknown).</summary>
    public double LatencyMs = -1;
    /// <summary>Where <see cref="LatencyMs"/> came from: "server ping" (measured here), "client ping" (the page reported it), "CIP heartbeat", "CIP handshake".</summary>
    public string? LatencySource;
    private readonly double[] _latencyRing = new double[30];
    private int _latencyCount;
    private readonly object _latencyGate = new();

    /// <summary>Record a round-trip measurement. Keeps the last 30 so the rack view can show a trend, min, average and max.</summary>
    public void RecordLatency(double ms, string source)
    {
        if (ms < 0 || double.IsNaN(ms)) return;
        lock (_latencyGate) { _latencyRing[_latencyCount++ % _latencyRing.Length] = ms; }
        LatencyMs = ms;
        LatencySource = source;
    }

    /// <summary>The last measurements (oldest first), at most 30.</summary>
    public double[] LatencyHistory()
    {
        lock (_latencyGate)
        {
            var n = Math.Min(_latencyCount, _latencyRing.Length);
            var res = new double[n];
            for (var i = 0; i < n; i++) res[i] = _latencyRing[(_latencyCount - n + i) % _latencyRing.Length];
            return res;
        }
    }

    public DateTimeOffset LastActivity => new(Interlocked.Read(ref _lastActivityTicks), TimeSpan.Zero);

    public void RecordIn(int bytes)
    {
        Interlocked.Increment(ref MessagesIn);
        Interlocked.Add(ref BytesIn, bytes);
        Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);
    }

    public void RecordOut(int bytes)
    {
        Interlocked.Increment(ref MessagesOut);
        Interlocked.Add(ref BytesOut, bytes);
    }

    /// <summary>Anything else learned about the client: discovery fields, what the panel page reported, console output.</summary>
    public IReadOnlyDictionary<string, string> Details => new SortedDictionary<string, string>(_details, StringComparer.OrdinalIgnoreCase);

    public void SetDetail(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) _details.TryRemove(key, out _);
        else _details[key] = value.Length > 8000 ? value[..8000] + "…" : value;
    }

    public string? GetDetail(string key) => _details.TryGetValue(key, out var v) ? v : null;

    /// <summary>Address as people write it: "10.0.0.5:51234", "[::1]:51234".</summary>
    public string Endpoint => RemoteAddress.Length == 0 ? "" :
        RemotePort == 0 ? RemoteAddress :
        RemoteAddress.Contains(':') ? $"[{RemoteAddress}]:{RemotePort}" : $"{RemoteAddress}:{RemotePort}";

    /// <summary>Short "who is this" text for console output and logs.</summary>
    public string Summary
    {
        get
        {
            var who = Hostname ?? Model ?? Peer ?? ClientKind ?? Transport;
            var what = ClientKind != null && ClientKind != who ? $" ({ClientKind})" : "";
            return $"{who}{what} {Endpoint}".Trim();
        }
    }
}
