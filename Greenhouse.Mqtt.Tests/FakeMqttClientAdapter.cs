using System.Collections.Concurrent;

namespace Greenhouse.Mqtt.Tests;

/// <summary>
/// In-memory <see cref="IMqttClientAdapter"/> for testing <see cref="MqttMessagingService"/>
/// without a broker. Records operations and lets a test drive inbound messages and disconnects.
/// </summary>
internal sealed class FakeMqttClientAdapter : IMqttClientAdapter
{
    private readonly ConcurrentQueue<(string Topic, string Payload)> _published = new();
    private readonly List<string> _subscribed = new();
    private readonly List<string> _unsubscribed = new();

    public bool IsConnected { get; private set; }

    public int ConnectCount { get; private set; }

    public IReadOnlyList<(string Topic, string Payload)> Published => _published.ToArray();

    public IReadOnlyList<string> Subscribed
    {
        get { lock (_subscribed) { return _subscribed.ToArray(); } }
    }

    public IReadOnlyList<string> Unsubscribed
    {
        get { lock (_unsubscribed) { return _unsubscribed.ToArray(); } }
    }

    public bool FailNextConnect { get; set; }

    public event Func<MqttInboundMessage, Task>? MessageReceived;

    public event Func<Task>? Disconnected;

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        ConnectCount++;
        if (FailNextConnect)
        {
            FailNextConnect = false;
            throw new InvalidOperationException("Simulated connect failure.");
        }

        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task PublishAsync(string topic, string payload, CancellationToken cancellationToken)
    {
        _published.Enqueue((topic, payload));
        return Task.CompletedTask;
    }

    public Task SubscribeAsync(string topicPattern, CancellationToken cancellationToken)
    {
        lock (_subscribed) { _subscribed.Add(topicPattern); }
        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(string topicPattern, CancellationToken cancellationToken)
    {
        lock (_unsubscribed) { _unsubscribed.Add(topicPattern); }
        return Task.CompletedTask;
    }

    /// <summary>Simulates the broker delivering a message.</summary>
    public Task RaiseMessageAsync(string topic, string payload) =>
        MessageReceived?.Invoke(new MqttInboundMessage(topic, payload)) ?? Task.CompletedTask;

    /// <summary>Simulates the connection dropping.</summary>
    public Task RaiseDisconnectedAsync()
    {
        IsConnected = false;
        return Disconnected?.Invoke() ?? Task.CompletedTask;
    }

    public void ClearSubscribed()
    {
        lock (_subscribed) { _subscribed.Clear(); }
    }
}
