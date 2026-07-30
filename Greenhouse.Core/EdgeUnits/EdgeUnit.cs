namespace Greenhouse.Core.EdgeUnits;

/// <summary>
/// A registered Edge Unit and its last-known slot topology. The Main Unit is the source of
/// truth for runtime mapping; the unit itself persists nothing beyond provisioning values.
/// </summary>
/// <param name="DeviceId">Edge Unit hardware identity (WiFi MAC address).</param>
/// <param name="AdvertisedName">Name the unit advertised during onboarding (<c>GH-Edge-{device_id}</c>).</param>
/// <param name="UnitName">Operator-assigned unit name; <c>null</c> until a mapping is accepted.</param>
/// <param name="Location">Operator-assigned location; <c>null</c> until a mapping is accepted.</param>
/// <param name="MappingVersion">Increases by exactly one per accepted mapping update; <c>0</c> before the first.</param>
/// <param name="MappingStatus">One of <see cref="MappingStatuses"/>.</param>
/// <param name="FirstSeenAt">UTC timestamp of the first heartbeat that registered the unit.</param>
/// <param name="LastHeartbeatAt">UTC timestamp of the most recent heartbeat.</param>
/// <param name="TopologyDriftDetectedAt">
/// UTC timestamp at which reported topology last diverged from the acknowledged mapping, or
/// <c>null</c> when there is no drift. This is the Drift Flag: it stays set until a replacement
/// mapping is acknowledged with <c>result=success</c>, and the previously acknowledged mapping
/// stays active for the whole time it is set.
/// </param>
/// <param name="Slots">Last-known slot topology, ascending by slot id.</param>
public sealed record EdgeUnit(
    string DeviceId,
    string AdvertisedName,
    string? UnitName,
    string? Location,
    int MappingVersion,
    string MappingStatus,
    DateTime FirstSeenAt,
    DateTime? LastHeartbeatAt,
    DateTime? TopologyDriftDetectedAt,
    IReadOnlyList<EdgeUnitSlot> Slots)
{
    /// <summary>True while reported topology diverges from the acknowledged mapping.</summary>
    public bool HasTopologyDrift => TopologyDriftDetectedAt is not null;
}

/// <summary>
/// A runtime mapping submitted for an Edge Unit. Carries only operator-assigned values; the
/// observed topology it is applied to comes from the stored heartbeat state.
/// </summary>
public sealed record EdgeUnitMapping(
    string UnitName,
    string Location,
    IReadOnlyList<SlotMapping> Slots);

/// <summary>Operator-assigned mapping for a single discovered slot.</summary>
public sealed record SlotMapping(int SlotId, string Role, string Capability, string? Label);
