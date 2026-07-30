using Greenhouse.Core.EdgeUnits;

namespace Greenhouse.Core.Tests.EdgeUnits;

/// <summary>
/// Covers the Main Unit input validation rules. Every rule here blocks the configuration publish,
/// so a gap would let an invalid mapping reach an Edge Unit.
/// </summary>
public class MappingValidationTests
{
    private static readonly DateTime ObservedAt = new(2026, 7, 1, 22, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<EdgeUnitSlot> Discovered(params (int SlotId, string Address)[] slots) =>
        slots.Select(s => new EdgeUnitSlot(s.SlotId, s.Address, null, null, null, ObservedAt)).ToArray();

    private static EdgeUnitMapping Mapping(
        string unitName = "East Sensor Unit",
        string location = "Zone A",
        params SlotMapping[] slots) =>
        new(unitName, location, slots);

    private static SlotMapping Slot(int slotId, string role = SlotRoles.Sensor, string capability = "moisture") =>
        new(slotId, role, capability, null);

    [Fact]
    public void Valid_mapping_produces_no_errors()
    {
        var errors = MappingValidation.Validate(
            Mapping(slots: new[] { Slot(0), Slot(4, SlotRoles.Actuator, "pump") }),
            Discovered((0, "0x25"), (4, "0x51")));

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("", "Zone A", "unitName")]
    [InlineData("   ", "Zone A", "unitName")]
    [InlineData("East Sensor Unit", "", "location")]
    [InlineData("East Sensor Unit", "   ", "location")]
    public void Blank_identity_fields_are_rejected(string unitName, string location, string expectedField)
    {
        var errors = MappingValidation.Validate(
            Mapping(unitName, location, Slot(0)),
            Discovered((0, "0x25")));

        Assert.Contains(errors, e => e.Field == expectedField);
    }

    [Fact]
    public void Empty_slot_list_is_rejected()
    {
        var errors = MappingValidation.Validate(Mapping(), Discovered((0, "0x25")));

        Assert.Contains(errors, e => e.Field == "slots");
    }

    [Fact]
    public void Duplicate_slot_ids_are_rejected()
    {
        var errors = MappingValidation.Validate(
            Mapping(slots: new[] { Slot(0), Slot(0) }),
            Discovered((0, "0x25")));

        Assert.Contains(errors, e => e.Field == "slots[1].slotId" && e.Message.Contains("more than once"));
    }

    [Fact]
    public void A_discovered_slot_missing_from_the_mapping_is_rejected()
    {
        var errors = MappingValidation.Validate(
            Mapping(slots: Slot(0)),
            Discovered((0, "0x25"), (4, "0x51")));

        Assert.Contains(errors, e => e.Field == "slots" && e.Message.Contains("slot 4"));
    }

    [Fact]
    public void A_slot_that_was_never_discovered_is_rejected()
    {
        var errors = MappingValidation.Validate(
            Mapping(slots: new[] { Slot(0), Slot(7) }),
            Discovered((0, "0x25")));

        Assert.Contains(errors, e => e.Field == "slots[1].slotId" && e.Message.Contains("not discovered"));
    }

    [Theory]
    [InlineData("controller")]
    [InlineData("")]
    [InlineData("Sensor")]
    public void A_role_outside_the_vocabulary_is_rejected(string role)
    {
        var errors = MappingValidation.Validate(
            Mapping(slots: Slot(0, role)),
            Discovered((0, "0x25")));

        Assert.Contains(errors, e => e.Field == "slots[0].role");
    }

    [Theory]
    [InlineData("humidity")]
    [InlineData("pump")]
    [InlineData("ec-tds")]
    public void Canonical_capabilities_are_accepted(string capability)
    {
        var errors = MappingValidation.Validate(
            Mapping(slots: Slot(0, SlotRoles.Sensor, capability)),
            Discovered((0, "0x25")));

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("wetness")]
    [InlineData("")]
    [InlineData("Moisture")]
    public void A_capability_outside_the_vocabulary_is_rejected(string capability)
    {
        var errors = MappingValidation.Validate(
            Mapping(slots: Slot(0, SlotRoles.Sensor, capability)),
            Discovered((0, "0x25")));

        Assert.Contains(errors, e => e.Field == "slots[0].capability");
    }

    [Fact]
    public void A_slot_whose_reported_i2c_address_is_invalid_is_rejected()
    {
        var errors = MappingValidation.Validate(
            Mapping(slots: Slot(0)),
            Discovered((0, "0x99")));

        Assert.Contains(errors, e => e.Message.Contains("invalid I2C address"));
    }

    [Theory]
    [InlineData("0x00", true)]
    [InlineData("0x25", true)]
    [InlineData("0x7F", true)]
    [InlineData("0x7f", true)]
    [InlineData("0x80", false)]
    [InlineData("0xFF", false)]
    [InlineData("25", false)]
    [InlineData("0x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void I2c_addresses_are_accepted_only_in_the_addressable_range(string? address, bool expected)
    {
        Assert.Equal(expected, MappingValidation.IsValidI2cAddress(address));
    }
}
