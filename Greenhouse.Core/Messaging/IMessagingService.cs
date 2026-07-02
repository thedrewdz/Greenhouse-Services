namespace Greenhouse.Core.Messaging;

/// <summary>
/// Generic message transport contract for the Main Unit runtime. It is message
/// type/content agnostic: it moves opaque payloads on caller-chosen topics and never knows
/// whether a message is a heartbeat, telemetry item, acknowledgement, or onboarding event.
/// </summary>
/// <remarks>
/// Topic-specific behaviour (for example publishing a heartbeat or handling an
/// acknowledgement) belongs in subscriber services or application handlers that register with
/// this service — never as methods on this interface. Subscribers register at startup via the
/// runtime composition root, not from inside request handlers or use-case constructors.
/// Topic patterns follow MQTT wildcard conventions (<c>+</c> single-level, <c>#</c> multi-level).
/// </remarks>
public interface IMessagingService
{
    /// <summary>Publishes <paramref name="payload"/> to <paramref name="topic"/>.</summary>
    Task PublishAsync(string topic, string payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers <paramref name="handler"/> for every message whose topic matches
    /// <paramref name="topicPattern"/>. May be called before the transport connects; the
    /// subscription is applied (and re-applied after any reconnect) once connected.
    /// </summary>
    void Subscribe(string topicPattern, Func<MessageEnvelope, Task> handler);

    /// <summary>Removes the handler previously registered for <paramref name="topicPattern"/>.</summary>
    void Unsubscribe(string topicPattern);
}
