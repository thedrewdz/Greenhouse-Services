namespace Greenhouse.Core.Messaging;

/// <summary>
/// Transport metadata for a single message crossing the messaging boundary. Carries the raw
/// payload only — never a parsed domain type. Subscribers are responsible for parsing,
/// validating, and routing the payload through the correct application use case.
/// </summary>
/// <param name="Topic">The exact topic the message was received on.</param>
/// <param name="Payload">The raw payload string, exactly as received; not parsed.</param>
/// <param name="ReceivedAt">UTC timestamp of receipt.</param>
public sealed record MessageEnvelope(string Topic, string Payload, DateTime ReceivedAt);
