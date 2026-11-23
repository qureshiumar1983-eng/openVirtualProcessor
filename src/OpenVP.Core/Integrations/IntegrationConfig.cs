using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace OpenVP.Core.Integrations;

/// <summary>MQTT broker connection (the password is kept apart, see <see cref="SecretStore"/>). Saved in <c>data/integrations/global.json</c>.</summary>
public sealed record MqttSettings
{
    public bool Enabled { get; init; }
    public string Host { get; init; } = "";
    public int Port { get; init; } = 1883;
    public bool Tls { get; init; }
    public string? Username { get; init; }
    /// <summary>MQTT client id; empty means <c>openvp-&lt;host name&gt;</c>.</summary>
    public string? ClientId { get; init; }
    /// <summary>Topics start with this; empty means <c>openvp/&lt;host name&gt;</c>.</summary>
    public string? BaseTopic { get; init; }
    /// <summary>Publish Home Assistant MQTT discovery for the signals that ask for it.</summary>
    public bool HomeAssistant { get; init; }
    public string DiscoveryPrefix { get; init; } = "homeassistant";

    public static string HostName() => Regex.Replace(Environment.MachineName.ToLowerInvariant(), "[^a-z0-9_-]", "-").Trim('-') is { Length: > 0 } h ? h : "openvp";
    public string EffectiveBase => string.IsNullOrWhiteSpace(BaseTopic) ? "openvp/" + HostName() : BaseTopic!.Trim().Trim('/');
    public string EffectiveClientId => string.IsNullOrWhiteSpace(ClientId) ? "openvp-" + HostName() : ClientId!.Trim();

    public MqttSettings Validated()
    {
        var host = Host?.Trim() ?? "";
        if (Enabled && host.Length == 0) throw new ArgumentException("MQTT needs a broker host name or address.");
        if (host.Length > 253 || host.Any(c => char.IsWhiteSpace(c) || c is '/' or '\\')) throw new ArgumentException("The broker host isn't valid. Use a name or address, without http:// or a port.");
        if (Port is < 1 or > 65535) throw new ArgumentException("The broker port must be 1 to 65535.");
        var b = BaseTopic?.Trim().Trim('/');
        if (!string.IsNullOrEmpty(b) && (b.Length > 200 || b.Contains('+') || b.Contains('#') || b.Contains("//") || b.Any(char.IsControl)))
            throw new ArgumentException("The base topic can't contain + or # or empty levels.");
        var cid = ClientId?.Trim();
        if (!string.IsNullOrEmpty(cid) && (cid.Length > 120 || cid.Any(char.IsControl))) throw new ArgumentException("The client id is too long or has control characters.");
        var dp = (DiscoveryPrefix ?? "").Trim().Trim('/');
        if (dp.Length == 0) dp = "homeassistant";
        if (dp.Length > 100 || dp.Contains('+') || dp.Contains('#')) throw new ArgumentException("The discovery prefix isn't valid.");
        var user = string.IsNullOrWhiteSpace(Username) ? null : Username.Trim();
        return this with { Host = host, BaseTopic = string.IsNullOrEmpty(b) ? null : b, ClientId = string.IsNullOrEmpty(cid) ? null : cid, DiscoveryPrefix = dp, Username = user };
    }
}

/// <summary>OSC settings: the UDP port to listen on (0 = off) and where to send. Saved in <c>global.json</c>.</summary>
public sealed record OscSettings
{
    public int ListenPort { get; init; }
    public string TargetHost { get; init; } = "";
    public int TargetPort { get; init; } = 9000;

    public OscSettings Validated()
    {
        if (ListenPort is < 0 or > 65535) throw new ArgumentException("The OSC listen port must be 0 (off) or 1 to 65535.");
        var host = TargetHost?.Trim() ?? "";
        if (host.Length > 253 || host.Any(c => char.IsWhiteSpace(c) || c is '/' or '\\')) throw new ArgumentException("The OSC target host isn't valid.");
        if (host.Length > 0 && TargetPort is < 1 or > 65535) throw new ArgumentException("The OSC target port must be 1 to 65535.");
        return this with { TargetHost = host };
    }
}

/// <summary>Art-Net settings. Saved in <c>global.json</c>.</summary>
public sealed record ArtNetSettings
{
    public bool Enabled { get; init; }
    /// <summary>Where ArtDMX goes: a node's address, or a broadcast address such as 2.255.255.255 or 255.255.255.255.</summary>
    public string Target { get; init; } = "255.255.255.255";
    public int Port { get; init; } = 6454;
    /// <summary>Frames per second per universe.</summary>
    public int RateHz { get; init; } = 30;
    /// <summary>Also listen on UDP 6454 for ArtDMX to set signals that are mapped for receiving.</summary>
    public bool Listen { get; init; }

    public ArtNetSettings Validated()
    {
        var t = Target?.Trim() ?? "";
        if (t.Length == 0 || t.Length > 253 || t.Any(c => char.IsWhiteSpace(c) || c is '/' or '\\')) throw new ArgumentException("The Art-Net target must be an address or host name.");
        if (Port is < 1 or > 65535) throw new ArgumentException("The Art-Net port must be 1 to 65535.");
        if (RateHz is < 1 or > 44) throw new ArgumentException("The Art-Net rate must be 1 to 44 frames per second.");
        return this with { Target = t };
    }
}

public sealed record GlobalIntegrations
{
    public MqttSettings Mqtt { get; init; } = new();
    public OscSettings Osc { get; init; } = new();
    public ArtNetSettings ArtNet { get; init; } = new();
}

/// <summary>A signal the MQTT bridge publishes. <see cref="Set"/> lets <c>…/set</c> change it; <see cref="Discovery"/> adds it to Home Assistant.</summary>
public sealed record MqttSignal
{
    public string Signal { get; init; } = "";
    public bool Set { get; init; }
    public bool Discovery { get; init; }
}

/// <summary>A signal sent over OSC and/or set by incoming OSC.</summary>
public sealed record OscSignal
{
    public string Signal { get; init; } = "";
    public bool Send { get; init; } = true;
    public bool Receive { get; init; }
}

/// <summary>An analog signal mapped to a DMX channel. <see cref="Mode"/> <c>8</c> scales 0-65535 to 0-255; <c>16</c> uses
/// <see cref="Channel"/> as the coarse byte and the next channel as the fine byte.</summary>
public sealed record ArtNetChannel
{
    public string Signal { get; init; } = "";
    public int Universe { get; init; }
    public int Channel { get; init; } = 1;
    public string Mode { get; init; } = "8";
    public bool Send { get; init; } = true;
    public bool Receive { get; init; }
}

/// <summary>An outgoing webhook. It fires when <see cref="Signal"/> changes, or when <see cref="Expression"/> becomes true.</summary>
public sealed record WebhookRule
{
    public string Id { get; init; } = "";
    public string? Name { get; init; }
    public string Url { get; init; } = "";
    public Dictionary<string, string> Headers { get; init; } = new();
    /// <summary>HMAC-SHA256 key; when set, each request carries <c>X-OpenVP-Signature</c>. Never sent back by the API.</summary>
    public string? Secret { get; init; }
    public string? Signal { get; init; }
    public string? Expression { get; init; }
    public bool Enabled { get; init; } = true;

    public string Label => string.IsNullOrWhiteSpace(Name) ? (Signal ?? Expression ?? Id) : Name!;
}

/// <summary>Everything one slot has set up for integrations. Saved in the slot's <c>devices/integrations.json</c>, so it travels with snapshots.</summary>
public sealed record SlotIntegrations
{
    public const int MaxItems = 500, MaxWebhooks = 50;
    public List<MqttSignal> Mqtt { get; init; } = new();
    public List<OscSignal> Osc { get; init; } = new();
    public List<ArtNetChannel> ArtNet { get; init; } = new();
    public List<WebhookRule> Webhooks { get; init; } = new();

    public static List<MqttSignal> CheckMqtt(IEnumerable<MqttSignal>? list)
    {
        var l = Dedupe(list ?? [], x => x.Signal, "MQTT").ToList();
        return l;
    }

    public static List<OscSignal> CheckOsc(IEnumerable<OscSignal>? list) => Dedupe(list ?? [], x => x.Signal, "OSC").ToList();

    public static List<ArtNetChannel> CheckArtNet(IEnumerable<ArtNetChannel>? list)
    {
        var l = (list ?? []).ToList();
        if (l.Count > MaxItems) throw new ArgumentException($"At most {MaxItems} Art-Net channels per slot.");
        var res = new List<ArtNetChannel>();
        foreach (var c0 in l)
        {
            var c = c0 with { Signal = c0.Signal?.Trim() ?? "", Mode = (c0.Mode ?? "8").Trim() };
            if (c.Signal.Length == 0) throw new ArgumentException("Every Art-Net channel needs a signal.");
            if (c.Mode is not ("8" or "16")) throw new ArgumentException("Art-Net mode is 8 or 16 (bits).");
            if (c.Universe is < 0 or > 32767) throw new ArgumentException("An Art-Net universe is 0 to 32767.");
            var max = c.Mode == "16" ? 511 : 512;
            if (c.Channel < 1 || c.Channel > max) throw new ArgumentException($"A {c.Mode}-bit channel must be 1 to {max}{(c.Mode == "16" ? " (it also uses the next channel)" : "")}.");
            if (!c.Send && !c.Receive) throw new ArgumentException($"{c.Signal}: choose send, receive or both.");
            res.Add(c);
        }
        // No two mappings may drive the same channel on a universe (a 16-bit one takes two).
        var taken = new Dictionary<(int, int), string>();
        foreach (var c in res.Where(c => c.Send))
            for (var ch = c.Channel; ch < c.Channel + (c.Mode == "16" ? 2 : 1); ch++)
                if (!taken.TryAdd((c.Universe, ch), c.Signal))
                    throw new ArgumentException($"Universe {c.Universe} channel {ch} is used by both {taken[(c.Universe, ch)]} and {c.Signal}.");
        return res;
    }

    private static IEnumerable<T> Dedupe<T>(IEnumerable<T> list, Func<T, string> signal, string what)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var n = 0;
        foreach (var x in list)
        {
            var s = signal(x)?.Trim() ?? "";
            if (s.Length == 0) throw new ArgumentException($"Every {what} entry needs a signal.");
            if (++n > MaxItems) throw new ArgumentException($"At most {MaxItems} {what} signals per slot.");
            if (!seen.Add(s)) continue;
            yield return x switch
            {
                MqttSignal m => (T)(object)(m with { Signal = s }),
                OscSignal o => (T)(object)(o with { Signal = s }),
                _ => x
            };
        }
    }

    /// <summary>Checks webhook rules, gives new ones an id and keeps the saved secret of a rule whose secret is left out.</summary>
    public static List<WebhookRule> CheckWebhooks(IEnumerable<WebhookRule>? list, IReadOnlyList<WebhookRule> existing)
    {
        var res = new List<WebhookRule>();
        var l = (list ?? []).ToList();
        if (l.Count > MaxWebhooks) throw new ArgumentException($"At most {MaxWebhooks} webhooks per slot.");
        foreach (var r0 in l)
        {
            var id = string.IsNullOrWhiteSpace(r0.Id) ? Guid.NewGuid().ToString("N")[..8] : r0.Id.Trim();
            if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]{1,40}$")) throw new ArgumentException("A webhook id can only have letters, digits, _ and -.");
            if (res.Any(x => x.Id == id)) throw new ArgumentException($"Two webhooks have the id {id}.");
            var url = r0.Url?.Trim() ?? "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || string.IsNullOrEmpty(u.Host))
                throw new ArgumentException($"{(string.IsNullOrWhiteSpace(r0.Name) ? "A webhook" : r0.Name)}: the URL must start with http:// or https://.");
            var signal = string.IsNullOrWhiteSpace(r0.Signal) ? null : r0.Signal.Trim();
            var expr = string.IsNullOrWhiteSpace(r0.Expression) ? null : r0.Expression.Trim();
            if ((signal == null) == (expr == null)) throw new ArgumentException($"{(string.IsNullOrWhiteSpace(r0.Name) ? url : r0.Name)}: choose either a signal or a condition.");
            if (expr != null && !SignalExpression.TryParse(expr, out _, out var err)) throw new ArgumentException($"Condition '{expr}': {err}");
            var headers = new Dictionary<string, string>();
            foreach (var (k, v) in r0.Headers ?? new())
            {
                var name = k.Trim();
                if (name.Length == 0) continue;
                if (!Regex.IsMatch(name, "^[A-Za-z0-9-]{1,60}$")) throw new ArgumentException($"'{name}' isn't a valid header name.");
                if ((v ?? "").Any(c => c is '\r' or '\n')) throw new ArgumentException($"The value of header {name} can't have line breaks.");
                if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) || name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) || name.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                headers[name] = v ?? "";
            }
            var secret = r0.Secret;
            if (secret == null) secret = existing.FirstOrDefault(e => e.Id == id)?.Secret;
            else if (secret.Length == 0) secret = null;
            res.Add(r0 with { Id = id, Url = url, Signal = signal, Expression = expr, Headers = headers, Secret = secret, Name = string.IsNullOrWhiteSpace(r0.Name) ? null : r0.Name.Trim() });
        }
        return res;
    }
}

/// <summary>Reads and writes the integration settings files, in one step each so a crash can't leave half a file.</summary>
public static class IntegrationFiles
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static T Load<T>(string path) where T : new()
    {
        try
        {
            if (!File.Exists(path)) return new T();
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? new T();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { return new T(); }
    }

    public static void Save<T>(string path, T value, bool ownerOnly = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        if (ownerOnly && !OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tmp, path, true);
    }
}
