namespace Greenhouse.Mqtt;

/// <summary>
/// Strongly-typed MQTT broker configuration, bound from the <c>Mqtt</c> section of
/// <c>appsettings.json</c> (with environment-variable overrides such as <c>MQTT__HOST</c>).
/// <see cref="MqttMessagingService"/> reads its connection settings from
/// <c>IOptions&lt;MqttOptions&gt;</c> — never directly from <c>IConfiguration</c>.
/// </summary>
public sealed class MqttOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Mqtt";

    /// <summary>Broker hostname or IP address.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>Broker TCP port.</summary>
    public int Port { get; set; } = 1883;

    /// <summary>Client identifier, unique per process instance.</summary>
    public string ClientId { get; set; } = "greenhouse-runtime";
}
