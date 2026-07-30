namespace Greenhouse.Core.EdgeUnits;

/// <summary>
/// Single source of the runtime mapping rules from
/// <c>specs/edge-unit-configuration/spec.md</c> ("Main Unit Input Validation"). Shared by the
/// API boundary, which turns the result into a field-level error envelope, and the
/// <see cref="UpdateEdgeUnitMapping"/> use case, which uses it as a backstop.
/// </summary>
/// <remarks>
/// Validation is against the unit's stored topology, not just the request: every discovered
/// slot must be mapped, and the I2C address checked is the one the unit reported.
/// </remarks>
public static class MappingValidation
{
    /// <summary>Returns one entry per broken rule; empty when the mapping is valid.</summary>
    public static IReadOnlyList<(string Field, string Message)> Validate(
        EdgeUnitMapping mapping,
        IReadOnlyList<EdgeUnitSlot> discoveredSlots)
    {
        var errors = new List<(string Field, string Message)>();

        if (string.IsNullOrWhiteSpace(mapping.UnitName))
        {
            errors.Add(("unitName", "Unit name is required."));
        }

        if (string.IsNullOrWhiteSpace(mapping.Location))
        {
            errors.Add(("location", "Location is required."));
        }

        if (mapping.Slots.Count == 0)
        {
            errors.Add(("slots", "At least one slot mapping is required."));
            return errors;
        }

        var seen = new HashSet<int>();
        for (var index = 0; index < mapping.Slots.Count; index++)
        {
            var slot = mapping.Slots[index];
            var field = $"slots[{index}]";

            if (!seen.Add(slot.SlotId))
            {
                errors.Add(($"{field}.slotId", $"Slot {slot.SlotId} is mapped more than once."));
            }

            if (!SlotRoles.IsValid(slot.Role))
            {
                errors.Add(($"{field}.role", "Role must be 'sensor' or 'actuator'."));
            }

            if (!Capabilities.IsCanonical(slot.Capability))
            {
                errors.Add(($"{field}.capability", $"'{slot.Capability}' is not a canonical capability."));
            }

            var discovered = discoveredSlots.FirstOrDefault(d => d.SlotId == slot.SlotId);
            if (discovered is null)
            {
                errors.Add(($"{field}.slotId", $"Slot {slot.SlotId} was not discovered on this Edge Unit."));
            }
            else if (!IsValidI2cAddress(discovered.I2cAddress))
            {
                errors.Add((
                    $"{field}.slotId",
                    $"Slot {slot.SlotId} reported an invalid I2C address '{discovered.I2cAddress}'."));
            }
        }

        foreach (var discovered in discoveredSlots)
        {
            if (!seen.Contains(discovered.SlotId))
            {
                errors.Add(("slots", $"Discovered slot {discovered.SlotId} is missing from the mapping."));
            }
        }

        return errors;
    }

    /// <summary>Accepts <c>0x00</c> through <c>0x7F</c> — the addressable I2C range.</summary>
    public static bool IsValidI2cAddress(string? address) =>
        address is not null
        && address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        && address.Length is 3 or 4
        && int.TryParse(
            address[2..],
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
        && value is >= 0x00 and <= 0x7F;
}
