using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using OpenVP.Core.Integrations;

namespace OpenVP.Server;

/// <summary>The MQTT bridge's connection to a broker, using MQTTnet 4 (MIT).</summary>
public sealed class MqttNetTransport : IMqttTransport
{
    private readonly IMqttClient _client = new MqttFactory().CreateMqttClient();

    public MqttNetTransport()
    {
        _client.ApplicationMessageReceivedAsync += e =>
        {
            MessageReceived?.Invoke(e.ApplicationMessage.Topic, e.ApplicationMessage.PayloadSegment.ToArray(), e.ApplicationMessage.Retain);
            return Task.CompletedTask;
        };
        _client.DisconnectedAsync += e =>
        {
            Disconnected?.Invoke(e.Exception?.Message ?? e.ReasonString ?? "The broker closed the connection.");
            return Task.CompletedTask;
        };
    }

    public bool IsConnected => _client.IsConnected;
    public event Action<string, byte[], bool>? MessageReceived;
    public event Action<string>? Disconnected;

    public async Task ConnectAsync(MqttConnectOptions o, CancellationToken ct)
    {
        var b = new MqttClientOptionsBuilder()
            .WithTcpServer(o.Host, o.Port)
            .WithClientId(o.ClientId)
            .WithCleanSession()
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithTimeout(TimeSpan.FromSeconds(10))
            .WithWillTopic(o.WillTopic)
            .WithWillPayload(System.Text.Encoding.UTF8.GetBytes(o.WillPayload))
            .WithWillRetain(true)
            .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce);
        if (!string.IsNullOrEmpty(o.Username)) b.WithCredentials(o.Username, o.Password ?? "");
        if (o.Tls) b.WithTlsOptions(t => t.UseTls());
        var r = await _client.ConnectAsync(b.Build(), ct);
        if (r.ResultCode != MqttClientConnectResultCode.Success) throw new IOException($"The broker refused the connection: {r.ResultCode}{(string.IsNullOrEmpty(r.ReasonString) ? "" : " (" + r.ReasonString + ")")}.");
    }

    public Task PublishAsync(string topic, byte[] payload, bool retain, int qos, CancellationToken ct) =>
        _client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload).WithRetainFlag(retain)
            .WithQualityOfServiceLevel(qos >= 1 ? MqttQualityOfServiceLevel.AtLeastOnce : MqttQualityOfServiceLevel.AtMostOnce).Build(), ct);

    public async Task SubscribeAsync(IEnumerable<string> topics, CancellationToken ct)
    {
        var b = new MqttClientSubscribeOptionsBuilder();
        foreach (var t in topics) b.WithTopicFilter(f => f.WithTopic(t).WithAtMostOnceQoS());
        await _client.SubscribeAsync(b.Build(), ct);
    }

    public async Task UnsubscribeAsync(IEnumerable<string> topics, CancellationToken ct)
    {
        var b = new MqttClientUnsubscribeOptionsBuilder();
        foreach (var t in topics) b.WithTopicFilter(t);
        await _client.UnsubscribeAsync(b.Build(), ct);
    }

    public Task DisconnectAsync(CancellationToken ct) => _client.DisconnectAsync(new MqttClientDisconnectOptions(), ct);

    public void Dispose() => _client.Dispose();
}
