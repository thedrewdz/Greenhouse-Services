namespace Greenhouse.Core.EdgeUnits;

/// <summary>
/// The only two slot roles accepted by mapping validation and published on
/// <c>ghcfg/wr-{device_id}</c>.
/// </summary>
public static class SlotRoles
{
    public const string Sensor = "sensor";

    public const string Actuator = "actuator";

    public static bool IsValid(string? role) =>
        role is Sensor or Actuator;
}

/// <summary>
/// Canonical <c>mapping_reason</c> values for a runtime configuration publish. Snake_case
/// because these cross the MQTT boundary verbatim.
/// </summary>
public static class MappingReasons
{
    /// <summary>First mapping stored for a newly onboarded Edge Unit.</summary>
    public const string InitialRegistration = "initial_registration";

    /// <summary>Mapping replaced after user reconfiguration or detected topology drift.</summary>
    public const string TopologyChange = "topology_change";
}
