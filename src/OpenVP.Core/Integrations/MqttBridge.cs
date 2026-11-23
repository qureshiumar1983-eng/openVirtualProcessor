using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace OpenVP.Core.Integrations;

/// <summary>What the bridge needs from an MQTT client library, so the bridge can be tested without a network.</summary>
public interface IMqttTransport : IDisposable
{
    bool IsConnected { get; }
    /// <summary>A message arrived: topic, payload, and whether the broker sent it as a retained message.</summary>
    event Action<string, byte[], bool>? MessageReceived;
    /// <summary>The connection was lost (with a reason).</summary>
    event Action<string>? Disconnected;
    Task ConnectAsync(MqttConnectOptions options, CancellationToken ct);
    Task PublishAsync(string topic, byte[] payload, bool retain, int qos, CancellationToken ct);
    Task SubscribeAsync(IEnumerable<string> topics, CancellationToken ct);
    Task UnsubscribeAsync(IEnumerable<string> topics, CancellationToken ct);
    Task DisconnectAsync(CancellationToken ct);
}

public sealed record MqttConnectOptions(string Host, int Port, bool Tls, string? Username, string? Password, string ClientId, string WillTopic, string WillPayload);

public sealed record MqttStatus(string State, string? Error, DateTimeOffset? ConnectedSince, DateTimeOffset? LastMessageIn, DateTimeOffset? LastMessageOut,
    string? LastTopic, long Published, long Received, long Dropped, int Signals, string BaseTopic, int Attempts);

/// <summary>Topic names and Home Assistant discovery payloads. Pure functions, so they are tested without a broker.</summary>
public static class MqttTopics
{
    public static string Availability(string baseTopic) => baseTopic + "/status";
    public static string SlotState(string baseTopic, int slot) => $"{baseTopic}/slot{slot:00}/state";
    public static string Value(string baseTopic, int slot, string segment) => $"{baseTopic}/slot{slot:00}/{segment}";
    public static string Set(string baseTopic, int slot, string segment) => Value(baseTopic, slot, segment) + "/set";

    public static string NodeId(MqttSettings s) => "openvp_" + MqttSettings.HostName().Replace('-', '_');
    public static string ObjectId(int slot, string segment) => $"slot{slot:00}_{segment}".Replace('.', '_').Replace('-', '_');

    public static string DiscoveryComponent(SigKind kind, bool settable) => kind switch
    {
        SigKind.Digital => settable ? "switch" : "binary_sensor",
        SigKind.Analog => settable ? "number" : "sensor",
        _ => "sensor"
    };

    public static string DiscoveryTopic(MqttSettings s, string component, int slot, string segment) =>
        $"{s.DiscoveryPrefix}/{component}/{NodeId(s)}/{ObjectId(slot, segment)}/config";

    /// <summary>The retained config message Home Assistant reads: a switch for digital, a number for analog, a text sensor for serial.</summary>
    public static string DiscoveryPayload(MqttSettings s, ProgramSlot slot, string signal, SigKind kind, bool settable, string segment)
    {
        var b = s.EffectiveBase;
        var node = NodeId(s);
        var unique = $"{node}_{ObjectId(slot.Number, segment)}";
        var o = new Dictionary<string, object?>
        {
            ["name"] = signal,
            ["unique_id"] = unique,
            ["state_topic"] = Value(b, slot.Number, segment),
            ["availability_topic"] = Availability(b),
            ["payload_available"] = "online",
            ["payload_not_available"] = "offline",
            ["device"] = new Dictionary<string, object?>
            {
                ["identifiers"] = new[] { $"{node}_slot{slot.Number:00}" },
                ["name"] = $"OpenVP slot {slot.Number:00}{(string.IsNullOrWhiteSpace(slot.Name) ? "" : " " + slot.Name)}",
                ["manufacturer"] = "OpenVP",
                ["model"] = slot.Runtime?.ProgramName ?? "virtual processor slot"
            }
        };
        // Only a switch and a number take commands; Home Assistant's sensors don't have a command topic.
        if (DiscoveryComponent(kind, settable) is "switch" or "number") o["command_topic"] = Set(b, slot.Number, segment);
        switch (kind)
        {
            case SigKind.Digital:
                if (settable) { o["payload_on"] = "1"; o["payload_off"] = "0"; o["state_on"] = "1"; o["state_off"] = "0"; }
                else { o["payload_on"] = "1"; o["payload_off"] = "0"; }
                break;
            case SigKind.Analog:
                if (settable) { o["min"] = 0; o["max"] = 65535; o["step"] = 1; o["mode"] = "box"; }
                break;
        }
        return JsonSerializer.Serialize(o);
    }
}

/// <summary>
/// Publishes chosen signals to an MQTT broker (retained, on change) and sets them from <c>…/set</c> topics.
/// <code>
/// &lt;base&gt;/status                 online / offline (the will), retained
/// &lt;base&gt;/slotNN/state            running, stopped, loaded, empty or error, retained
/// &lt;base&gt;/slotNN/&lt;signal&gt;        the value: 1/0, a number, or text; retained
/// &lt;base&gt;/slotNN/&lt;signal&gt;/set    write here to set it (when the signal allows it)
/// </code>
/// Changes reach the bridge through <see cref="SignalHub"/> and a bounded queue; publishing happens on the bridge's own
/// task, so a slow or dead broker never holds up a program. When the queue is full the oldest entries are dropped (and counted).
/// </summary>
public sealed class MqttBridge : IDisposable
{
    private enum K { Signal, SyncSlot, SyncAll }
    private readonly record struct Item(K Kind, int Slot, string? Name, SigKind SigKind, object? Value);

    private sealed record Entry(MqttSignal Cfg, string Segment, string Topic, string SetTopic);
    private sealed class Plan
    {
        public MqttSettings Settings = new();
        public string? Password;
        public string Base = "";
        public Dictionary<int, Dictionary<string, Entry>> Slots = new();
        public Dictionary<string, (int Slot, string Signal)> SetTopics = new(StringComparer.Ordinal);
    }

    private readonly Processor _processor;
    private readonly IntegrationStore _store;
    private readonly SignalDirectory _directory;
    private readonly Func<IMqttTransport> _factory;
    private readonly object _gate = new();       // status fields
    private readonly object _applyGate = new();  // Apply and Dispose, one at a time
    private readonly Channel<Item> _queue;
    private readonly List<IDisposable> _subs = new();
    private readonly HashSet<string> _discovered = new(StringComparer.Ordinal);
    private volatile Plan _plan = new();
    private CancellationTokenSource? _cts;
    private Task? _task;
    private string _state = "off";
    private string? _error;
    private DateTimeOffset? _connectedSince, _lastIn, _lastOut;
    private string? _lastTopic;
    private long _published, _received, _dropped;
    private int _attempts;
    private volatile bool _connected;
    private long _lastWarn;
    private bool _disposed;
    private readonly Action<ProgramSlot> _onSlotState;
    private TimeSpan _maxBackoff = TimeSpan.FromSeconds(30);

    public MqttBridge(Processor processor, IntegrationStore store, SignalDirectory directory, Func<IMqttTransport> factory)
    {
        _processor = processor; _store = store; _directory = directory; _factory = factory;
        _queue = Channel.CreateBounded<Item>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            _ => Interlocked.Increment(ref _dropped));
        _onSlotState = s => { if (_connected && _plan.Slots.ContainsKey(s.Number)) _queue.Writer.TryWrite(new Item(K.SyncSlot, s.Number, null, default, null)); };
        foreach (var s in processor.Slots) s.StateChanged += _onSlotState;
    }

    /// <summary>For tests: how long to wait between reconnect attempts (at most).</summary>
    public TimeSpan MaxBackoff { get => _maxBackoff; set => _maxBackoff = value; }

    public MqttStatus Status()
    {
        var p = _plan;
        lock (_gate)
            return new MqttStatus(_state, _error, _connectedSince, _lastIn, _lastOut, _lastTopic, Interlocked.Read(ref _published), Interlocked.Read(ref _received),
                Interlocked.Read(ref _dropped), p.Slots.Sum(x => x.Value.Count), p.Base, _attempts);
    }

    /// <summary>Read the settings and each slot's signals again: connects, reconnects, disconnects or just re-syncs, as needed.</summary>
    public void Apply()
    {
        lock (_applyGate)
        {
            if (_disposed) return;
            var next = BuildPlan();
            var old = _plan;
            _plan = next;
            ResubscribeHub(next);
            var wanted = next.Settings.Enabled && next.Settings.Host.Length > 0;
            var sameConn = old.Settings.Enabled == next.Settings.Enabled && old.Settings.Host == next.Settings.Host && old.Settings.Port == next.Settings.Port
                && old.Settings.Tls == next.Settings.Tls && old.Settings.Username == next.Settings.Username && old.Settings.EffectiveClientId == next.Settings.EffectiveClientId
                && old.Base == next.Base && old.Password == next.Password;
            if (!wanted)
            {
                StopWorker();
                lock (_gate) { _state = "off"; _error = null; }
                return;
            }
            if (_task == null || !sameConn) { StopWorker(); StartWorker(); }
            else _queue.Writer.TryWrite(new Item(K.SyncAll, 0, null, default, null));
        }
    }

    private Plan BuildPlan()
    {
        var g = _store.Global.Mqtt;
        var p = new Plan { Settings = g, Base = g.EffectiveBase, Password = _store.Secrets.Get("mqtt.password") };
        foreach (var slot in _processor.Slots)
        {
            var list = _store.Slot(slot.Number).Mqtt;
            if (list.Count == 0) continue;
            var segs = SignalValues.Segments(list.Select(x => x.Signal));
            var d = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (var m in list)
            {
                var seg = segs[m.Signal];
                var e = new Entry(m, seg, MqttTopics.Value(p.Base, slot.Number, seg), MqttTopics.Set(p.Base, slot.Number, seg));
                d[m.Signal] = e;
                if (m.Set) p.SetTopics[e.SetTopic] = (slot.Number, m.Signal);
            }
            p.Slots[slot.Number] = d;
        }
        return p;
    }

    // Called with the apply lock held. One hub subscription per slot, for exactly the chosen signals.
    private void ResubscribeHub(Plan p)
    {
        foreach (var s in _subs) s.Dispose();
        _subs.Clear();
        if (!(p.Settings.Enabled && p.Settings.Host.Length > 0)) return;
        foreach (var (slotNo, entries) in p.Slots)
        {
            var n = slotNo;
            _subs.Add(_processor.Slot(n).Hub.Subscribe(entries.Keys, e =>
            {
                if (_connected) _queue.Writer.TryWrite(new Item(K.Signal, n, e.Signal, e.Kind, e.Value));
            }));
        }
    }

    private void StartWorker()
    {
        _cts = new CancellationTokenSource();
        var cts = _cts; var plan = _plan;
        lock (_gate) { _state = "connecting"; _error = null; _attempts = 0; }
        _task = Task.Run(() => RunAsync(plan, cts.Token));
    }

    private void StopWorker()
    {
        var cts = _cts; var task = _task;
        _cts = null; _task = null; _connected = false;
        if (cts == null) return;
        cts.Cancel();
        try { task?.Wait(TimeSpan.FromSeconds(3)); } catch { }
        cts.Dispose();
    }

    private async Task RunAsync(Plan conn, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (!ct.IsCancellationRequested)
        {
            IMqttTransport? t = null;
            var lost = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var subscribed = new HashSet<string>(StringComparer.Ordinal);
            using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
            try
            {
                lock (_gate) { _state = "connecting"; _attempts++; }
                t = _factory();
                t.MessageReceived += OnMessage;
                t.Disconnected += why => lost.TrySetResult(why);
                var b = conn.Base;
                var s = conn.Settings;
                await t.ConnectAsync(new MqttConnectOptions(s.Host, s.Port, s.Tls, s.Username, conn.Password, s.EffectiveClientId, MqttTopics.Availability(b), "offline"), ct);
                lock (_gate) { _state = "connected"; _error = null; _connectedSince = DateTimeOffset.Now; }
                delay = TimeSpan.FromSeconds(1);
                await Put(t, MqttTopics.Availability(b), "online", true, 1, ct);
                // Whatever queued up while we were away is stale: the sync below publishes every value as it is now.
                while (_queue.Reader.TryRead(out _)) { }
                _connected = true;
                await SyncAll(t, subscribed, ct);
                while (true)
                {
                    var read = _queue.Reader.WaitToReadAsync(session.Token).AsTask();
                    if (await Task.WhenAny(read, lost.Task) == lost.Task) throw new IOException(lost.Task.Result);
                    if (!await read) break;
                    while (_queue.Reader.TryRead(out var item))
                    {
                        switch (item.Kind)
                        {
                            case K.Signal: await PublishValue(t, item, ct); break;
                            case K.SyncSlot: await SyncSlot(t, item.Slot, ct); await SyncSubscriptions(t, subscribed, ct); break;
                            case K.SyncAll: await SyncAll(t, subscribed, ct); break;
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception e)
            {
                lock (_gate) { _state = "error"; _error = e.Message; }
            }
            finally
            {
                _connected = false;
                session.Cancel();
                if (t != null)
                {
                    try
                    {
                        if (t.IsConnected)
                        {
                            using var quick = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                            // A clean disconnect doesn't send the will, so say it ourselves.
                            await t.PublishAsync(MqttTopics.Availability(conn.Base), Encoding.UTF8.GetBytes("offline"), true, 1, quick.Token);
                            await t.DisconnectAsync(quick.Token);
                        }
                    }
                    catch { }
                    t.Dispose();
                }
            }
            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { break; }
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _maxBackoff.Ticks));
        }
    }

    private async Task Put(IMqttTransport t, string topic, string payload, bool retain, int qos, CancellationToken ct)
    {
        await t.PublishAsync(topic, Encoding.UTF8.GetBytes(payload), retain, qos, ct);
        lock (_gate) { _lastOut = DateTimeOffset.Now; _lastTopic = topic; }
        Interlocked.Increment(ref _published);
    }

    private async Task SyncAll(IMqttTransport t, HashSet<string> subscribed, CancellationToken ct)
    {
        await SyncSubscriptions(t, subscribed, ct);
        foreach (var n in _plan.Slots.Keys.ToList()) await SyncSlot(t, n, ct);
        // Discovery entries of signals that are no longer chosen are removed from Home Assistant.
        var p = _plan;
        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (n, entries) in p.Slots)
            foreach (var e in entries.Values)
                if (p.Settings.HomeAssistant && e.Cfg.Discovery && _directory.TryGet(n, e.Cfg.Signal, out var info))
                    keep.Add(MqttTopics.DiscoveryTopic(p.Settings, MqttTopics.DiscoveryComponent(info.Kind, e.Cfg.Set), n, e.Segment));
        foreach (var gone in _discovered.Where(x => !keep.Contains(x)).ToList())
        {
            await Put(t, gone, "", true, 1, ct);
            _discovered.Remove(gone);
        }
    }

    private async Task SyncSubscriptions(IMqttTransport t, HashSet<string> subscribed, CancellationToken ct)
    {
        var want = _plan.SetTopics.Keys.ToHashSet(StringComparer.Ordinal);
        var add = want.Where(x => !subscribed.Contains(x)).ToList();
        var del = subscribed.Where(x => !want.Contains(x)).ToList();
        if (add.Count > 0) await t.SubscribeAsync(add, ct);
        if (del.Count > 0) await t.UnsubscribeAsync(del, ct);
        foreach (var x in add) subscribed.Add(x);
        foreach (var x in del) subscribed.Remove(x);
    }

    // The slot's state, its Home Assistant entries and the current value of each chosen signal.
    private async Task SyncSlot(IMqttTransport t, int n, CancellationToken ct)
    {
        var p = _plan;
        if (!p.Slots.TryGetValue(n, out var entries)) return;
        var slot = _processor.Slot(n);
        await Put(t, MqttTopics.SlotState(p.Base, n), slot.State.ToString().ToLowerInvariant(), true, 1, ct);
        var current = slot.Runtime == null ? new List<SignalInfo>() : _directory.All(n).Where(s => entries.ContainsKey(s.Name)).ToList();
        foreach (var s in current)
        {
            var e = entries[s.Name];
            await Put(t, e.Topic, SignalValues.Format(s.Value), true, 0, ct);
            if (p.Settings.HomeAssistant && e.Cfg.Discovery)
            {
                var comp = MqttTopics.DiscoveryComponent(s.Kind, e.Cfg.Set);
                var topic = MqttTopics.DiscoveryTopic(p.Settings, comp, n, e.Segment);
                await Put(t, topic, MqttTopics.DiscoveryPayload(p.Settings, slot, s.Name, s.Kind, e.Cfg.Set, e.Segment), true, 1, ct);
                _discovered.Add(topic);
            }
        }
    }

    private async Task PublishValue(IMqttTransport t, Item item, CancellationToken ct)
    {
        var p = _plan;
        if (!p.Slots.TryGetValue(item.Slot, out var entries) || !entries.TryGetValue(item.Name!, out var e)) return;
        await Put(t, e.Topic, SignalValues.Format(item.Value), true, 0, ct);
    }

    private void OnMessage(string topic, byte[] payload, bool retained)
    {
        lock (_gate) { _lastIn = DateTimeOffset.Now; _lastTopic = topic; }
        Interlocked.Increment(ref _received);
        // A retained message on a /set topic would otherwise be applied again at every reconnect.
        if (retained) return;
        if (!_plan.SetTopics.TryGetValue(topic, out var target)) return;
        var text = Encoding.UTF8.GetString(payload).Trim();
        var err = _directory.SetFromText(target.Slot, target.Signal, text);
        if (err != null) Warn(target.Slot, $"MQTT {topic}: {err}");
    }

    // At most one warning every few seconds, so a chatty broker can't flood the log.
    private void Warn(int slot, string message)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastWarn);
        if (now - last < 5000 || Interlocked.CompareExchange(ref _lastWarn, now, last) != last) return;
        _processor.Slot(slot).Log("Warning", message);
    }

    public void Dispose()
    {
        lock (_applyGate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
            StopWorker();
        }
        foreach (var s in _processor.Slots) s.StateChanged -= _onSlotState;
    }
}
