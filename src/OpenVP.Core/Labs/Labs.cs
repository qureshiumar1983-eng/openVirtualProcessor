using System.Text.Json;
using System.Text.Json.Serialization;
using OpenVP.Core.Integrations;

namespace OpenVP.Core.Labs;

/// <summary>One experimental feature on the Labs page.</summary>
/// <param name="Id">Stable name used in the API, the settings file and <c>OpenVP:Labs:&lt;id&gt;</c>.</param>
/// <param name="LegacyKey">An older configuration key (under <c>OpenVP:</c>) that already switched this on. While it says true the feature is on whatever the toggle says.</param>
public sealed record LabFeature(string Id, string Name, string Description, string Warning, bool Default = false, string? LegacyKey = null, bool NeedsRestart = false);

public static class LabsCatalog
{
    public const string RemoteEisc = "remote-eisc", Plugins = "plugins", CipHeartbeatProbe = "cip-heartbeat-probe";

    public static readonly IReadOnlyList<LabFeature> All =
    [
        new(RemoteEisc, "Remote EISC",
            "Link an EISC on this OpenVP to an EISC on another OpenVP machine so joins flow both ways over the network. One side opens an outbound WebSocket, authenticated with an API token (scope eisc-link) made on the other machine.",
            "The wire format and the settings may change between versions, and both machines need a version that agrees. Plain http carries the token and every join unencrypted."),
        new(Plugins, "Plugins",
            "Load plugins from data/plugins/<name>/: a .NET assembly that adds console commands, REST routes and signal hooks, or a JavaScript page that adds a menu item. Each plugin stays off until an Admin enables it.",
            "Plugin code runs inside this process with its full rights. The permissions a plugin asks for control what OpenVP's plugin API lets it do, not what the code can do. Only enable plugins you trust. The plugin API may change."),
        new(CipHeartbeatProbe, "CIP heartbeat probe",
            "Measures a CIP panel's round-trip time continuously from its heartbeats, so the rack view shows live latency instead of only the figure from the connect handshake.",
            "Untested against real panels. Applies to panels that connect after you switch it on.", LegacyKey: "CipHeartbeatProbe"),
    ];
}

/// <summary>Which Labs features are on. Kept in <c>data/labs.json</c>; <c>OpenVP:Labs:&lt;id&gt;</c> (and a feature's older key) in the configuration wins over the file.</summary>
public sealed class LabsStore
{
    private sealed class FileDto { public Dictionary<string, bool> Features { get; set; } = new(); }
    private readonly string? _file;
    private readonly Func<string, bool?> _config;
    private readonly object _gate = new();
    private FileDto _data;

    /// <summary>Raised after a feature was switched, with its id and new state.</summary>
    public event Action<string, bool>? Changed;

    /// <param name="config">Looks up a configuration key (for example <c>Labs:remote-eisc</c>, relative to <c>OpenVP:</c>); null when it isn't set.</param>
    public LabsStore(string? dataDirectory, Func<string, bool?>? config = null)
    {
        _file = dataDirectory == null ? null : Path.Combine(dataDirectory, "labs.json");
        _config = config ?? (_ => null);
        _data = _file == null ? new() : IntegrationFiles.Load<FileDto>(_file);
    }

    public static LabFeature? Find(string id) => LabsCatalog.All.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The configuration that forces a feature, as <c>(value, key)</c>; null when nothing does.</summary>
    public (bool Value, string Key)? ForcedBy(string id)
    {
        var f = Find(id) ?? throw new ArgumentException("There is no Labs feature " + id);
        if (_config("Labs:" + f.Id) is { } v) return (v, "OpenVP:Labs:" + f.Id);
        if (f.LegacyKey != null && _config(f.LegacyKey) is { } lv && lv) return (true, "OpenVP:" + f.LegacyKey);
        return null;
    }

    public bool IsOn(string id)
    {
        if (ForcedBy(id) is { } forced) return forced.Value;
        var f = Find(id) ?? throw new ArgumentException("There is no Labs feature " + id);
        lock (_gate) return _data.Features.TryGetValue(f.Id, out var on) ? on : f.Default;
    }

    /// <summary>Switches a feature. Throws if the configuration forces it (change that instead) or the id is unknown.</summary>
    public void Set(string id, bool on)
    {
        var f = Find(id) ?? throw new ArgumentException("There is no Labs feature " + id);
        if (ForcedBy(f.Id) is { } forced) throw new InvalidOperationException($"{forced.Key} is set in the configuration, so it can't be changed here.");
        bool was;
        lock (_gate)
        {
            was = _data.Features.TryGetValue(f.Id, out var w) ? w : f.Default;
            _data.Features[f.Id] = on;
            if (_file != null) { try { IntegrationFiles.Save(_file, _data); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
        }
        if (was != on) Changed?.Invoke(f.Id, on);
    }

    public IReadOnlyList<object> List() => LabsCatalog.All.Select(f => (object)new
    {
        id = f.Id, name = f.Name, description = f.Description, warning = f.Warning, enabled = IsOn(f.Id), @default = f.Default,
        forcedBy = ForcedBy(f.Id)?.Key, needsRestart = f.NeedsRestart
    }).ToList();
}
