using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MQTTnet;

namespace Greenhouse.Mqtt;

/// <summary>
/// <see cref="IMqttClientAdapter"/> implemented over the MQTTnet client. This is the only type
/// that touches MQTTnet; everything above it works in terms of the internal seam. Connection
/// policy (retry, resubscribe) lives in <see cref="MqttMessagingService"/>; this type performs
/// single operations and forwards broker events.
/// </summary>
internal sealed class MqttNetClientAdapter : IMqttClientAdapter, IDisposable
{
    private readonly IMqttClient _client;
    private readonly MqttOptions _options;
    private readonly ILogger<MqttNetClientAdapter> _logger;

    public MqttNetClientAdapter(IOptions<MqttOptions> options, ILogger<MqttNetClientAdapter> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new MqttClientFactory().CreateMqttClient();

        _client.ApplicationMessageReceivedAsync += OnApplicationMessageReceivedAsync;
        _client.DisconnectedAsync += OnClientDisconnectedAsync;
    }

    public bool IsConnected => _client.IsConnected;

    public event Func<MqttInboundMessage, Task>? MessageReceived;

    public event Func<Task>? Disconnected;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_options.Host, _options.Port)
            .WithClientId(_options.ClientId)
            .WithCleanSession()
            .Build();

        await _client.ConnectAsync(options, cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.DisconnectAsync(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MQTT client disconnect raised an error.");
        }
    }

    public Task PublishAsync(string topic, string payload, CancellationToken cancellationToken)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .Build();

        return _client.PublishAsync(message, cancellationToken);
    }

    public Task SubscribeAsync(string topicPattern, CancellationToken cancellationToken)
    {
        var options = new MqttClientSubscribeOptionsBuilder()
            .WithTopicFilter(f => f.WithTopic(topicPattern))
            .Build();

        return _client.SubscribeAsync(options, cancellationToken);
    }

    public Task UnsubscribeAsync(string topicPattern, CancellationToken cancellationToken)
    {
        var options = new MqttClientUnsubscribeOptionsBuilder()
            .WithTopicFilter(topicPattern)
            .Build();

        return _client.UnsubscribeAsync(options, cancellationToken);
    }

    private async Task OnApplicationMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var handler = MessageReceived;
        if (handler is null)
        {
            return;
        }

        var inbound = new MqttInboundMessage(
            args.ApplicationMessage.Topic,
            args.ApplicationMessage.ConvertPayloadToString() ?? string.Empty);

        await handler(inbound);
    }

    private async Task OnClientDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        var handler = Disconnected;
        if (handler is not null)
        {
            await handler();
        }
    }

    public void Dispose() => _client.Dispose();
}
