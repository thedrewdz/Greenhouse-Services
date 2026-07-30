namespace Greenhouse.Storage.Entities;

/// <summary>
/// EF Core entity mapped to the <c>SlotTopologies</c> table — one row per discovered slot on an
/// Edge Unit. Infrastructure-only: must not be referenced outside <c>Greenhouse.Storage</c>.
/// </summary>
/// <remarks>
/// <see cref="SlotId"/> and <see cref="I2cAddress"/> are observed from heartbeats;
/// <see cref="Role"/>, <see cref="Capability"/>, and <see cref="Label"/> are assigned by an
/// accepted mapping and stay null until then.
/// </remarks>
internal sealed class SlotTopologyEntity
{
    public int Id { get; set; }

    public int EdgeUnitId { get; set; }

    public EdgeUnitEntity EdgeUnit { get; set; } = null!;

    public int SlotId { get; set; }

    public string I2cAddress { get; set; } = null!;

    public string? Role { get; set; }

    public string? Capability { get; set; }

    public string? Label { get; set; }

    public DateTime ObservedAt { get; set; }
}
