namespace Greenhouse.Core.EdgeUnits;

/// <summary>
/// Outbound port for delivering an accepted runtime mapping to its Edge Unit. Publishing runs
/// asynchronously after the mapping has been stored, so the caller returns immediately and the
/// operator observes publish and acknowledgement progress through the live channel.
/// </summary>
public interface IEdgeUnitConfigurationPublisher
{
    /// <summary>
    /// Queues a configuration publish for <paramref name="deviceId"/>. Returns as soon as the
    /// request is queued; delivery, acknowledgement, and the bounded retry budget are handled in
    /// the background.
    /// </summary>
    /// <param name="mappingReason">One of <see cref="MappingReasons"/>.</param>
    void RequestPublish(string deviceId, string mappingReason);
}

/// <summary>
/// The bounded retry budget for a configuration publish
/// (<c>specs/edge-unit-configuration/spec.md</c>, "Runtime Configuration Ack Handling").
/// Injected rather than hard-coded so tests can exercise the retry paths without waiting.
/// </summary>
/// <param name="AckTimeout">How long to wait for an acknowledgement per publish attempt.</param>
/// <param name="RetryDelays">
/// Delay before each retry. Its length plus one is the total attempt budget.
/// </param>
public sealed record ConfigurationPublishPolicy(TimeSpan AckTimeout, IReadOnlyList<TimeSpan> RetryDelays)
{
    /// <summary>The canonical Phase 1 budget: 8-second ack timeout, 3 attempts, 1s then 2s apart.</summary>
    public static ConfigurationPublishPolicy Default { get; } = new(
        TimeSpan.FromSeconds(8),
        new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2) });

    /// <summary>Total publish attempts per configuration update.</summary>
    public int MaxAttempts => RetryDelays.Count + 1;
}
