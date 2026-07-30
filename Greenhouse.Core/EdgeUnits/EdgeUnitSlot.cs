namespace Greenhouse.Core.EdgeUnits;

/// <summary>
/// One slot of a registered Edge Unit: the topology the unit reported plus the runtime mapping
/// the operator assigned to it.
/// </summary>
/// <remarks>
/// <paramref name="SlotId"/> and <paramref name="I2cAddress"/> are observed — they come from the
/// heartbeat and identify the physical module. <paramref name="Role"/>,
/// <paramref name="Capability"/>, and <paramref name="Label"/> are assigned and stay
/// <c>null</c> until a mapping is accepted.
/// </remarks>
/// <param name="SlotId">Slot index on the Edge Unit.</param>
/// <param name="I2cAddress">Module I2C address as reported by the unit (e.g. <c>0x25</c>).</param>
/// <param name="Role">Assigned role: <c>sensor</c> or <c>actuator</c>.</param>
/// <param name="Capability">Assigned canonical capability name.</param>
/// <param name="Label">Optional operator-facing display label.</param>
/// <param name="ObservedAt">UTC timestamp of the heartbeat this topology came from.</param>
public sealed record EdgeUnitSlot(
    int SlotId,
    string I2cAddress,
    string? Role,
    string? Capability,
    string? Label,
    DateTime ObservedAt);
