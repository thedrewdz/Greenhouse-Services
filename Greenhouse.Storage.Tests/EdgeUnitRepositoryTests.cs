using Greenhouse.Core.EdgeUnits;
using Greenhouse.Storage.Repositories;

namespace Greenhouse.Storage.Tests;

public class EdgeUnitRepositoryTests
{
    private static readonly DateTime SeenAt = new(2026, 7, 1, 22, 0, 0, DateTimeKind.Utc);

    private static EdgeUnit Registered(string deviceId = "1ADD5912AF61", params EdgeUnitSlot[] slots) => new(
        deviceId,
        "GH-Edge-" + deviceId,
        UnitName: null,
        Location: null,
        MappingVersion: 0,
        MappingStatus: MappingStatuses.PendingMapping,
        FirstSeenAt: SeenAt,
        LastHeartbeatAt: SeenAt,
        TopologyDriftDetectedAt: null,
        Slots: slots);

    private static EdgeUnitSlot Slot(int slotId, string i2cAddress) =>
        new(slotId, i2cAddress, Role: null, Capability: null, Label: null, SeenAt);

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_device()
    {
        using var db = new SqliteTestDatabase();

        Assert.Null(await new EdgeUnitRepository(db.Database).GetAsync("nope"));
    }

    [Fact]
    public async Task UpsertAsync_inserts_then_GetAsync_maps_every_field()
    {
        using var db = new SqliteTestDatabase();
        var repository = new EdgeUnitRepository(db.Database);

        await repository.UpsertAsync(Registered(slots: new[] { Slot(0, "0x25"), Slot(4, "0x51") }));
        var loaded = await repository.GetAsync("1ADD5912AF61");

        Assert.NotNull(loaded);
        Assert.Equal("GH-Edge-1ADD5912AF61", loaded!.AdvertisedName);
        Assert.Equal(MappingStatuses.PendingMapping, loaded.MappingStatus);
        Assert.Equal(SeenAt, loaded.FirstSeenAt);
        Assert.Equal(SeenAt, loaded.LastHeartbeatAt);
        Assert.Null(loaded.TopologyDriftDetectedAt);
        Assert.Equal(new[] { 0, 4 }, loaded.Slots.Select(s => s.SlotId));
        Assert.Equal("0x51", loaded.Slots[1].I2cAddress);
    }

    [Fact]
    public async Task UpsertAsync_updates_the_existing_row_rather_than_inserting()
    {
        using var db = new SqliteTestDatabase();
        var repository = new EdgeUnitRepository(db.Database);

        await repository.UpsertAsync(Registered(slots: Slot(0, "0x25")));
        await repository.UpsertAsync(Registered(slots: Slot(0, "0x25")) with
        {
            LastHeartbeatAt = SeenAt.AddMinutes(1),
        });

        Assert.Equal(1, db.CountRows("EdgeUnits"));
        var loaded = await repository.GetAsync("1ADD5912AF61");
        Assert.Equal(SeenAt.AddMinutes(1), loaded!.LastHeartbeatAt);
    }

    [Fact]
    public async Task UpsertAsync_removes_slots_that_are_no_longer_reported()
    {
        using var db = new SqliteTestDatabase();
        var repository = new EdgeUnitRepository(db.Database);

        await repository.UpsertAsync(Registered(slots: new[] { Slot(0, "0x25"), Slot(4, "0x51") }));
        await repository.UpsertAsync(Registered(slots: Slot(0, "0x25")));

        var loaded = await repository.GetAsync("1ADD5912AF61");
        Assert.Equal(0, Assert.Single(loaded!.Slots).SlotId);
        Assert.Equal(1, db.CountRows("SlotTopologies"));
    }

    [Fact]
    public async Task GetAllAsync_returns_every_registered_unit()
    {
        using var db = new SqliteTestDatabase();
        var repository = new EdgeUnitRepository(db.Database);

        await repository.UpsertAsync(Registered("2BEEF0000001", Slot(0, "0x25")));
        await repository.UpsertAsync(Registered("1ADD5912AF61", Slot(0, "0x25")));

        var all = await repository.GetAllAsync();

        Assert.Equal(new[] { "1ADD5912AF61", "2BEEF0000001" }, all.Select(u => u.DeviceId));
    }

    [Fact]
    public async Task UpdateMappingAsync_increments_the_version_and_assigns_slots()
    {
        using var db = new SqliteTestDatabase();
        var repository = new EdgeUnitRepository(db.Database);
        await repository.UpsertAsync(Registered(slots: new[] { Slot(0, "0x25"), Slot(4, "0x51") }));

        var updated = await repository.UpdateMappingAsync(
            "1ADD5912AF61",
            new EdgeUnitMapping(
                "East Sensor Unit",
                "Zone A",
                new[]
                {
                    new SlotMapping(0, SlotRoles.Sensor, "moisture", "Bed A Moisture"),
                    new SlotMapping(4, SlotRoles.Actuator, "pump", null),
                }));

        Assert.NotNull(updated);
        Assert.Equal(1, updated!.MappingVersion);
        Assert.Equal(MappingStatuses.PublishPending, updated.MappingStatus);
        Assert.Equal("East Sensor Unit", updated.UnitName);
        Assert.Equal("Zone A", updated.Location);
        Assert.Equal(SlotRoles.Sensor, updated.Slots[0].Role);
        Assert.Equal("Bed A Moisture", updated.Slots[0].Label);
        Assert.Equal("pump", updated.Slots[1].Capability);
        // The observed I2C address is not something a mapping may change.
        Assert.Equal("0x51", updated.Slots[1].I2cAddress);
    }

    [Fact]
    public async Task UpdateMappingAsync_returns_null_for_an_unknown_device()
    {
        using var db = new SqliteTestDatabase();

        var result = await new EdgeUnitRepository(db.Database).UpdateMappingAsync(
            "nope",
            new EdgeUnitMapping("Name", "Zone", Array.Empty<SlotMapping>()));

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateMappingStatusAsync_clears_the_drift_flag_only_when_asked()
    {
        using var db = new SqliteTestDatabase();
        var repository = new EdgeUnitRepository(db.Database);
        await repository.UpsertAsync(Registered(slots: Slot(0, "0x25")) with
        {
            TopologyDriftDetectedAt = SeenAt,
        });

        await repository.UpdateMappingStatusAsync("1ADD5912AF61", MappingStatuses.Published, clearTopologyDrift: false);
        var afterPublish = await repository.GetAsync("1ADD5912AF61");
        Assert.Equal(MappingStatuses.Published, afterPublish!.MappingStatus);
        Assert.True(afterPublish.HasTopologyDrift);

        await repository.UpdateMappingStatusAsync("1ADD5912AF61", MappingStatuses.Acknowledged, clearTopologyDrift: true);
        var afterAck = await repository.GetAsync("1ADD5912AF61");
        Assert.Equal(MappingStatuses.Acknowledged, afterAck!.MappingStatus);
        Assert.False(afterAck.HasTopologyDrift);
    }
}
