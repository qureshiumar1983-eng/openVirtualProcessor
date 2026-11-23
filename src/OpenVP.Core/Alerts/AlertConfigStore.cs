using System.Text.Json;
using OpenVP.Core.Integrations;

namespace OpenVP.Core.Alerts;

/// <summary>
/// Keeps the alert settings in <c>data/alerts/config.json</c> (channels without their secrets, rules, thresholds and the report
/// schedule) and the secrets in the <see cref="SecretStore"/>. A bad configuration is refused with a plain message and nothing changes.
/// </summary>
public sealed class AlertConfigStore
{
    private readonly string _file;
    private readonly object _gate = new();
    private AlertConfig _config;

    public SecretStore Secrets { get; }

    public AlertConfigStore(string dataDirectory, SecretStore secrets)
    {
        Secrets = secrets;
        _file = Path.Combine(dataDirectory, "alerts", "config.json");
        _config = Load();
    }

    public AlertConfig Current { get { lock (_gate) return _config; } }

    /// <summary>Raised (outside the lock) after the configuration changed.</summary>
    public event Action? Changed;

    private AlertConfig Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                var c = JsonSerializer.Deserialize<AlertConfig>(File.ReadAllText(_file), AlertJson.Options);
                if (c != null) return AlertConfigValidator.Validate(c);
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException) { }
        return AlertConfig.Default();
    }

    /// <summary>Validates and saves <paramref name="next"/>. <paramref name="secretChanges"/> maps a secret name to its new value (null or empty removes it).</summary>
    public AlertConfig Save(AlertConfig next, IReadOnlyDictionary<string, string?>? secretChanges = null)
    {
        var v = AlertConfigValidator.Validate(next);
        lock (_gate)
        {
            var old = _config;
            // Secrets of channels that no longer exist go too.
            foreach (var gone in old.Channels.Where(o => v.Channels.All(c => c.Id != o.Id)))
                foreach (var what in new[] { "password", "token", "secret", "url" }) Secrets.Set(gone.SecretName(what), null);
            // A channel whose type changed can't keep the old type's secrets.
            foreach (var c in v.Channels)
                if (old.Channel(c.Id) is { } o && o.Type != c.Type)
                    foreach (var what in new[] { "password", "token", "secret", "url" }) Secrets.Set(c.SecretName(what), null);
            if (secretChanges != null)
                foreach (var (name, value) in secretChanges) Secrets.Set(name, value);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
                var tmp = _file + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(v, new JsonSerializerOptions(AlertJson.Options) { WriteIndented = true }));
                File.Move(tmp, _file, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { throw new InvalidOperationException($"Couldn't save the alert settings: {e.Message}", e); }
            _config = v;
        }
        try { Changed?.Invoke(); } catch { }
        return v;
    }
}
