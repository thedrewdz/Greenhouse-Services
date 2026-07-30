using Greenhouse.Api.Contracts;
using Greenhouse.Api.Controllers;
using Greenhouse.Core.EdgeUnits;
using Microsoft.AspNetCore.Mvc;

namespace Greenhouse.Api.Tests;

/// <summary>
/// Contract tests for the Edge Unit resources, including the shared mapping endpoint used by
/// onboarding, operator reconfiguration, and topology-drift reconfiguration alike.
/// </summary>
public class EdgeUnitsControllerTests
{
    private const string DeviceId = "1ADD5912AF61";
    private static readonly DateTime SeenAt = new(2026, 7, 1, 22, 0, 0, DateTimeKind.Utc);

    private static EdgeUnit Registered(string deviceId = DeviceId, int mappingVersion = 0) => new(
        deviceId,
        "GH-Edge-" + deviceId,
        mappingVersion == 0 ? null : "East Sensor Unit",
        mappingVersion == 0 ? null : "Zone A",
        mappingVersion,
        mappingVersion == 0 ? MappingStatuses.PendingMapping : MappingStatuses.Acknowledged,
        SeenAt,
        SeenAt,
        TopologyDriftDetectedAt: null,
        Slots: new[]
        {
            new EdgeUnitSlot(0, "0x25", SlotRoles.Sensor, "moisture", "Bed A Moisture", SeenAt),
        });

    private static (EdgeUnitsController Controller, FakeEdgeUnitRepository Units, RecordingConfigurationPublisher Publisher)
        Create(params EdgeUnit[] seed)
    {
        var units = new FakeEdgeUnitRepository();
        foreach (var unit in seed)
        {
            units.Units[unit.DeviceId] = unit;
        }

        var publisher = new RecordingConfigurationPublisher();
        return (new EdgeUnitsController(units, new UpdateEdgeUnitMapping(units, publisher)), units, publisher);
    }

    private static EdgeUnitMappingRequest ValidRequest() => new(
        "East Sensor Unit",
        "Zone A",
        new[] { new SlotMappingRequest(0, SlotRoles.Sensor, "moisture", "Bed A Moisture") });

    [Fact]
    public async Task GetAll_returns_every_registered_unit()
    {
        var (controller, _, _) = Create(Registered("2BEEF0000001"), Registered());

        var result = await controller.GetAll(CancellationToken.None);

        var body = Assert.IsType<EdgeUnitListResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(new[] { DeviceId, "2BEEF0000001" }, body.EdgeUnits.Select(u => u.DeviceId));
        Assert.Equal(MappingStatuses.PendingMapping, body.EdgeUnits[0].MappingStatus);
        Assert.Equal(SeenAt, body.EdgeUnits[0].LastHeartbeatAt);
    }

    [Fact]
    public async Task GetAll_on_a_Main_Unit_with_no_units_returns_an_empty_list()
    {
        var (controller, _, _) = Create();

        var result = await controller.GetAll(CancellationToken.None);

        var body = Assert.IsType<EdgeUnitListResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Empty(body.EdgeUnits);
    }

    [Fact]
    public async Task Get_returns_the_detail_including_slot_topology()
    {
        var (controller, _, _) = Create(Registered(mappingVersion: 1));

        var result = await controller.Get(DeviceId, CancellationToken.None);

        var body = Assert.IsType<EdgeUnitDetailResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(1, body.MappingVersion);
        Assert.Equal(MappingStatuses.Acknowledged, body.MappingStatus);
        var slot = Assert.Single(body.Slots);
        Assert.Equal(0, slot.SlotId);
        Assert.Equal(SlotRoles.Sensor, slot.Role);
        Assert.Equal("moisture", slot.Capability);
        Assert.Equal("Bed A Moisture", slot.Label);
        Assert.Equal("0x25", slot.I2cAddress);
    }

    [Fact]
    public async Task Get_returns_404_for_an_unknown_device()
    {
        var (controller, _, _) = Create();

        var result = await controller.Get(DeviceId, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task PutMapping_stores_the_mapping_and_reports_publish_pending()
    {
        var (controller, _, publisher) = Create(Registered());

        var result = await controller.PutMapping(DeviceId, ValidRequest(), CancellationToken.None);

        var body = Assert.IsType<EdgeUnitMappingResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(DeviceId, body.DeviceId);
        Assert.Equal("East Sensor Unit", body.UnitName);
        Assert.Equal("Zone A", body.Location);
        Assert.Equal(1, body.MappingVersion);
        Assert.Equal(MappingStatuses.PublishPending, body.MappingStatus);
        // Publication is queued, not awaited: the response returns before delivery.
        Assert.Equal((DeviceId, MappingReasons.InitialRegistration), Assert.Single(publisher.Requests));
    }

    [Fact]
    public async Task PutMapping_returns_404_for_an_unknown_device()
    {
        var (controller, _, publisher) = Create();

        var result = await controller.PutMapping(DeviceId, ValidRequest(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
        Assert.Empty(publisher.Requests);
    }

    [Fact]
    public async Task PutMapping_returns_422_with_field_errors_for_a_missing_unit_name()
    {
        var (controller, _, publisher) = Create(Registered());

        var result = await controller.PutMapping(
            DeviceId,
            ValidRequest() with { UnitName = "   " },
            CancellationToken.None);

        var envelope = Assert.IsType<ValidationErrorEnvelope>(
            Assert.IsType<UnprocessableEntityObjectResult>(result).Value);
        Assert.Equal("validation-error", envelope.Type);
        Assert.Contains("unitName", envelope.Errors.Keys);
        Assert.Empty(publisher.Requests);
    }

    [Fact]
    public async Task PutMapping_returns_422_when_location_is_missing()
    {
        var (controller, _, _) = Create(Registered());

        var result = await controller.PutMapping(
            DeviceId,
            ValidRequest() with { Location = null },
            CancellationToken.None);

        var envelope = Assert.IsType<ValidationErrorEnvelope>(
            Assert.IsType<UnprocessableEntityObjectResult>(result).Value);
        Assert.Contains("location", envelope.Errors.Keys);
    }

    [Fact]
    public async Task PutMapping_returns_422_when_a_discovered_slot_is_unmapped()
    {
        var (controller, _, _) = Create(Registered());

        var result = await controller.PutMapping(
            DeviceId,
            ValidRequest() with { Slots = Array.Empty<SlotMappingRequest>() },
            CancellationToken.None);

        var envelope = Assert.IsType<ValidationErrorEnvelope>(
            Assert.IsType<UnprocessableEntityObjectResult>(result).Value);
        Assert.Contains("slots", envelope.Errors.Keys);
    }

    [Fact]
    public async Task PutMapping_returns_422_for_a_non_canonical_capability()
    {
        var (controller, _, _) = Create(Registered());

        var result = await controller.PutMapping(
            DeviceId,
            ValidRequest() with
            {
                Slots = new[] { new SlotMappingRequest(0, SlotRoles.Sensor, "wetness", null) },
            },
            CancellationToken.None);

        var envelope = Assert.IsType<ValidationErrorEnvelope>(
            Assert.IsType<UnprocessableEntityObjectResult>(result).Value);
        Assert.Contains("slots[0].capability", envelope.Errors.Keys);
    }

    [Fact]
    public async Task Re_mapping_a_configured_unit_publishes_as_a_topology_change()
    {
        var (controller, _, publisher) = Create(Registered(mappingVersion: 2));

        var result = await controller.PutMapping(DeviceId, ValidRequest(), CancellationToken.None);

        var body = Assert.IsType<EdgeUnitMappingResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(3, body.MappingVersion);
        Assert.Equal((DeviceId, MappingReasons.TopologyChange), Assert.Single(publisher.Requests));
    }
}
