namespace Greenhouse.Mqtt;

/// <summary>
/// A raw inbound message surfaced by the transport client: the topic it arrived on and its
/// undecoded payload string. Internal seam type — carries no MQTT library types.
/// </summary>
internal sealed record MqttInboundMessage(string Topic, string Payload);

/// <summary>
/// Thin internal seam over the underlying MQTT client library. It exposes only the primitive
/// operations <see cref="MqttMessagingService"/> needs, so that all MQTT library types stay
/// inside this project and the messaging service can be unit-tested against a fake.
/// </summary>
/// <remarks>
/// This adapter is deliberately "dumb": it performs a single connect/publish/subscribe call and
/// raises events. Reconnect scheduling, subscription bookkeeping, wildcard matching, and handler
/// dispatch are policy owned by <see cref="MqttMessagingService"/>.
/// </remarks>
internal interface IMqttClientAdapter
{
    /// <summary>True while the underlying client reports a live broker connection.</summary>
    bool IsConnected { get; }

    /// <summary>Raised for every message the broker delivers.</summary>
    event Func<MqttInboundMessage, Task>? MessageReceived;

    /// <summary>Raised when the connection drops (for any reason other than a requested stop).</summary>
    event Func<Task>? Disconnected;

    /// <summary>Opens a connection to the configured broker. Throws on failure.</summary>
    Task ConnectAsync(CancellationToken cancellationToken);

    /// <summary>Closes the connection cleanly. Never throws.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken);

    /// <summary>Publishes a payload to a topic on the live connection.</summary>
    Task PublishAsync(string topic, string payload, CancellationToken cancellationToken);

    /// <summary>Subscribes the live connection to a topic filter.</summary>
    Task SubscribeAsync(string topicPattern, CancellationToken cancellationToken);

    /// <summary>Unsubscribes the live connection from a topic filter.</summary>
    Task UnsubscribeAsync(string topicPattern, CancellationToken cancellationToken);
}
