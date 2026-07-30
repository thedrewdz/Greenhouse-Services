namespace Greenhouse.Core.EdgeUnits;

/// <summary>
/// Canonical Edge Unit topic names from <c>mqtt-topics.md</c>. Built in one place so no caller
/// hand-formats a topic.
/// </summary>
public static class EdgeUnitTopics
{
    /// <summary>Heartbeat topic every Edge Unit publishes on.</summary>
    public const string Heartbeat = "gh/heartbeat";

    /// <summary>Runtime configuration namespace, covering both the write and ack channels.</summary>
    public const string ConfigurationRoot = "ghcfg/#";

    private const string ConfigurationWritePrefix = "ghcfg/wr-";
    private const string ConfigurationAckPrefix = "ghcfg/ack-";

    /// <summary>Configuration write topic for one Edge Unit.</summary>
    public static string ConfigurationWrite(string deviceId) => ConfigurationWritePrefix + deviceId;

    /// <summary>Configuration acknowledgement topic for one Edge Unit.</summary>
    public static string ConfigurationAck(string deviceId) => ConfigurationAckPrefix + deviceId;

    /// <summary>
    /// Returns the device id when <paramref name="topic"/> is a configuration acknowledgement,
    /// otherwise <c>null</c>. Used to filter the shared <c>ghcfg/#</c> subscription, which also
    /// carries the Main Unit's own write messages.
    /// </summary>
    public static string? DeviceIdFromConfigurationAck(string topic) =>
        topic.StartsWith(ConfigurationAckPrefix, StringComparison.Ordinal)
            ? topic[ConfigurationAckPrefix.Length..]
            : null;
}
