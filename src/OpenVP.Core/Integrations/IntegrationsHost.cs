namespace OpenVP.Core.Integrations;

/// <summary>
/// Ties the integrations together: one store of settings and the five services. Create it once, call <see cref="Start"/>, and
/// call <see cref="Dispose"/> when the server stops. A change to the settings (or to a slot's list of signals) re-applies just
/// the service it belongs to.
/// </summary>
public sealed class IntegrationsHost : IDisposable
{
    public IntegrationStore Store { get; }
    public SignalDirectory Directory { get; }
    public MqttBridge Mqtt { get; }
    public WebhookService Webhooks { get; }
    public IncomingHooks Hooks { get; }
    public OscService Osc { get; }
    public ArtNetService ArtNet { get; }

    private long _scrapes;
    private DateTimeOffset? _lastScrape;
    public long Scrapes => Interlocked.Read(ref _scrapes);
    public DateTimeOffset? LastScrape => _lastScrape;
    public void NoteScrape() { Interlocked.Increment(ref _scrapes); _lastScrape = DateTimeOffset.Now; }

    public IntegrationsHost(Processor processor, Func<IMqttTransport> mqttFactory, HttpMessageHandler? webhookHandler = null, SecretStore? secrets = null)
    {
        Store = new IntegrationStore(processor, secrets);
        Directory = new SignalDirectory(processor);
        Mqtt = new MqttBridge(processor, Store, Directory, mqttFactory);
        Webhooks = new WebhookService(processor, Store, Directory, webhookHandler);
        Hooks = new IncomingHooks(processor, Directory);
        Osc = new OscService(processor, Store, Directory);
        ArtNet = new ArtNetService(processor, Store, Directory);
        Store.GlobalChanged += section =>
        {
            switch (section) { case "mqtt": Mqtt.Apply(); break; case "osc": Osc.Apply(); break; case "artnet": ArtNet.Apply(); break; }
        };
        Store.SlotChanged += n => { Mqtt.Apply(); Webhooks.ApplySlot(n); Osc.Apply(); ArtNet.Apply(); };
    }

    /// <summary>Start whatever the saved settings turn on.</summary>
    public void Start()
    {
        Mqtt.Apply();
        Webhooks.Apply();
        Osc.Apply();
        ArtNet.Apply();
    }

    public void Dispose()
    {
        Mqtt.Dispose(); Webhooks.Dispose(); Hooks.Dispose(); Osc.Dispose(); ArtNet.Dispose();
    }
}
