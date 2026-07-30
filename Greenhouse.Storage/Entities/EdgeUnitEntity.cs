namespace Greenhouse.Storage.Entities;

/// <summary>
/// EF Core entity mapped to the <c>EdgeUnits</c> table — one row per registered Edge Unit.
/// Infrastructure-only: must not be referenced outside <c>Greenhouse.Storage</c>.
/// </summary>
internal sealed class EdgeUnitEntity
{
    public int Id { get; set; }

    /// <summary>Edge Unit hardware identity; uniquely indexed.</summary>
    public string DeviceId { get; set; } = null!;

    public string AdvertisedName { get; set; } = null!;

    public string? UnitName { get; set; }

    public string? Location { get; set; }

    public int MappingVersion { get; set; }

    public string MappingStatus { get; set; } = null!;

    public DateTime FirstSeenAt { get; set; }

    public DateTime? LastHeartbeatAt { get; set; }

    /// <summary>The Drift Flag: set while reported topology diverges from the acknowledged mapping.</summary>
    public DateTime? TopologyDriftDetectedAt { get; set; }

    public List<SlotTopologyEntity> Slots { get; set; } = new();
}
