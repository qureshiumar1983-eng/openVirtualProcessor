namespace OpenVP.Core.Integrations;

/// <summary>
/// Where the integration settings live. Global settings (broker, OSC and Art-Net ports) are in
/// <c>data/integrations/global.json</c>; the MQTT password is in <see cref="SecretStore"/>; each slot's choices of signals
/// and its outgoing webhooks are in <c>data/programNN/devices/integrations.json</c>, which is part of the slot's snapshots.
/// </summary>
public sealed class IntegrationStore
{
    private readonly Processor _processor;
    private readonly string _globalFile;
    private readonly object _gate = new();
    private GlobalIntegrations _global;
    private readonly Dictionary<int, SlotIntegrations> _slots = new();

    public SecretStore Secrets { get; }

    public IntegrationStore(Processor processor, SecretStore? secrets = null)
    {
        _processor = processor;
        _globalFile = Path.Combine(processor.DataDirectory, "integrations", "global.json");
        Secrets = secrets ?? new SecretStore(processor.DataDirectory);
        _global = IntegrationFiles.Load<GlobalIntegrations>(_globalFile);
    }

    public GlobalIntegrations Global { get { lock (_gate) return _global; } }

    /// <summary>Raised (outside the lock) after settings change: <c>mqtt</c>, <c>osc</c> or <c>artnet</c>.</summary>
    public event Action<string>? GlobalChanged;
    /// <summary>Raised (outside the lock) after a slot's integration file changed or was reloaded.</summary>
    public event Action<int>? SlotChanged;

    private void SaveGlobal(GlobalIntegrations g)
    {
        try { IntegrationFiles.Save(_globalFile, g); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new InvalidOperationException($"Couldn't save the integration settings: {e.Message}", e); }
        _global = g;
    }

    /// <summary>Saves the MQTT settings. <paramref name="password"/> null keeps the saved one; <paramref name="clearPassword"/> removes it.</summary>
    public MqttSettings SetMqtt(MqttSettings s, string? password = null, bool clearPassword = false)
    {
        var v = s.Validated();
        lock (_gate)
        {
            if (clearPassword) Secrets.Set("mqtt.password", null);
            else if (!string.IsNullOrEmpty(password)) Secrets.Set("mqtt.password", password);
            SaveGlobal(_global with { Mqtt = v });
        }
        GlobalChanged?.Invoke("mqtt");
        return v;
    }

    public OscSettings SetOsc(OscSettings s)
    {
        var v = s.Validated();
        lock (_gate) SaveGlobal(_global with { Osc = v });
        GlobalChanged?.Invoke("osc");
        return v;
    }

    public ArtNetSettings SetArtNet(ArtNetSettings s)
    {
        var v = s.Validated();
        lock (_gate) SaveGlobal(_global with { ArtNet = v });
        GlobalChanged?.Invoke("artnet");
        return v;
    }

    private string SlotFile(int n) => Path.Combine(_processor.Slot(n).Directory, "devices", "integrations.json");

    public SlotIntegrations Slot(int n)
    {
        _processor.Slot(n); // throws for a bad number
        lock (_gate)
        {
            if (!_slots.TryGetValue(n, out var c)) _slots[n] = c = IntegrationFiles.Load<SlotIntegrations>(SlotFile(n));
            return c;
        }
    }

    /// <summary>Changes and saves a slot's integrations. <paramref name="change"/> returns the new value (and throws ArgumentException for a bad one).</summary>
    public SlotIntegrations UpdateSlot(int n, Func<SlotIntegrations, SlotIntegrations> change)
    {
        SlotIntegrations next;
        lock (_gate)
        {
            next = change(Slot(n));
            try { IntegrationFiles.Save(SlotFile(n), next); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { throw new InvalidOperationException($"Couldn't save the slot's integration settings: {e.Message}", e); }
            _slots[n] = next;
        }
        SlotChanged?.Invoke(n);
        return next;
    }

    /// <summary>Read the slot's file again (after a snapshot was restored).</summary>
    public void ReloadSlot(int n)
    {
        lock (_gate) _slots.Remove(n);
        SlotChanged?.Invoke(n);
    }
}
