using Greenhouse.Core.EdgeUnits;

namespace Greenhouse.Core.Tests.EdgeUnits;

public class UpdateEdgeUnitMappingTests
{
    private const string DeviceId = "1ADD5912AF61";
    private static readonly DateTime SeenAt = new(2026, 7, 1, 22, 0, 0, DateTimeKind.Utc);

    private static EdgeUnit Registered(int mappingVersion = 0) => new(
        DeviceId,
        "GH-Edge-" + DeviceId,
        UnitName: null,
        Location: null,
        mappingVersion,
        MappingStatuses.PendingMapping,
        SeenAt,
        SeenAt,
        TopologyDriftDetectedAt: null,
        Slots: new[]
        {
            new EdgeUnitSlot(0, "0x25", null, null, null, SeenAt),
            new EdgeUnitSlot(4, "0x51", null, null, null, SeenAt),
        });

    private static EdgeUnitMapping ValidMapping(string unitName = "East Sensor Unit") => new(
        unitName,
        "Zone A",
        new[]
        {
            new SlotMapping(0, SlotRoles.Sensor, "moisture", "Bed A Moisture"),
            new SlotMapping(4, SlotRoles.Actuator, "pump", null),
        });

    private static (UpdateEdgeUnitMapping UseCase, FakeEdgeUnitRepository Units, RecordingConfigurationPublisher Publisher)
        Create(EdgeUnit? seed)
    {
        var units = new FakeEdgeUnitRepository();
        if (seed is not null)
        {
            units.Units[seed.DeviceId] = seed;
        }

        var publisher = new RecordingConfigurationPublisher();
        return (new UpdateEdgeUnitMapping(units, publisher), units, publisher);
    }

    [Fact]
    public async Task Unknown_device_is_reported_without_publishing()
    {
        var (useCase, _, publisher) = Create(seed: null);

        var result = await useCase.ExecuteAsync(DeviceId, ValidMapping());

        Assert.IsType<UpdateMappingResult.UnknownDevice>(result);
        Assert.Empty(publisher.Requests);
    }

    [Fact]
    public async Task Accepted_mapping_increments_the_version_and_queues_a_publish()
    {
        var (useCase, _, publisher) = Create(Registered());

        var result = await useCase.ExecuteAsync(DeviceId, ValidMapping());

        var accepted = Assert.IsType<UpdateMappingResult.Accepted>(result);
        Assert.Equal(1, accepted.EdgeUnit.MappingVersion);
        Assert.Equal(MappingStatuses.PublishPending, accepted.EdgeUnit.MappingStatus);
        Assert.Equal((DeviceId, MappingReasons.InitialRegistration), Assert.Single(publisher.Requests));
    }

    [Fact]
    public async Task Replacing_an_existing_mapping_publishes_as_a_topology_change()
    {
        var (useCase, _, publisher) = Create(Registered(mappingVersion: 1));

        await useCase.ExecuteAsync(DeviceId, ValidMapping());

        Assert.Equal((DeviceId, MappingReasons.TopologyChange), Assert.Single(publisher.Requests));
    }

    [Fact]
    public async Task Invalid_mapping_neither_persists_nor_publishes()
    {
        var (useCase, units, publisher) = Create(Registered());

        var result = await useCase.ExecuteAsync(
            DeviceId,
            new EdgeUnitMapping(string.Empty, "Zone A", ValidMapping().Slots));

        var invalid = Assert.IsType<UpdateMappingResult.Invalid>(result);
        Assert.Contains(invalid.Errors, e => e.Field == "unitName");
        Assert.Empty(publisher.Requests);
        Assert.Equal(0, units.Units[DeviceId].MappingVersion);
        Assert.Equal(MappingStatuses.PendingMapping, units.Units[DeviceId].MappingStatus);
    }

    [Fact]
    public async Task Identity_fields_are_trimmed_before_validation_and_storage()
    {
        var (useCase, units, _) = Create(Registered());

        var result = await useCase.ExecuteAsync(
            DeviceId,
            ValidMapping() with { UnitName = "  East Sensor Unit  ", Location = "  Zone A  " });

        Assert.IsType<UpdateMappingResult.Accepted>(result);
        Assert.Equal("East Sensor Unit", units.Units[DeviceId].UnitName);
        Assert.Equal("Zone A", units.Units[DeviceId].Location);
    }
}
