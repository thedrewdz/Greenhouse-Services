using Greenhouse.Core.EdgeUnits;
using Greenhouse.Storage.Repositories;

namespace Greenhouse.Storage.Tests;

public class EdgeUnitRepositoryTests
{
    private const string DeviceId = "1ADD5912AF61";
    private static readonly DateTime SeenAt = new(2026, 7, 1, 22, 0, 0, DateTimeKind.Utc);

    private static HeartbeatMessage Heartbeat(params (int SlotId, string Address)[] slots) =>
        Heartbeat(DeviceId, slots);

    private static HeartbeatMessage Heartbeat(string deviceId, params (int SlotId, string Address)[] slots) =>
        new(
            deviceId,
            slots.Select(s => new HeartbeatSlot(s.SlotId, s.Address, "moisture")).ToArray());

    private static EdgeUnitMapping Mapping() => new(
        "East Sensor Unit",
        "Zone A",
        new[] { new SlotMapping(0, SlotRoles.Sensor, "moisture", "Bed A Moisture") });

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_device()
    {
        using var db = new SqliteTestDatabase();

        Assert.Null(await new EdgeUnitRepository(db.Database).GetAsync("nope"));
    }

    [Fact]
    public async Task RecordHeartbeatAsync_registers_an_unknown_unit_and_maps_every_field()
    {
        using var db = new SqliteTestDatabase();
        var repository = new EdgeUnitRepository(db.Database);

        var outcome = await repository.RecordHeartbeatAsync(Heartbeat((0, "0x25"), (4, "0x51")), SeenAt);
        var loaded = await repository.GetAsync(DeviceId);

        Assert.True(outcome.Registered);
        Assert.NotNull(loaded);
        Assert.Equal("GH-Edge-" + DeviceId, loaded!.AdvertisedName);
        Assert.Equal(MappingStatuses.PendingMapping, loaded.MappingStatus);
        Assert.Equal(0, loaded.MappingVersion);
        Assert.Equal(SeenAt, loaded.FirstSeenAt);
        Assert.Equal(SeenAt, loaded.LastHeartbeatAt);
        Assert.Null(loaded.TopologyDriftDetectedAt);
        Assert.Equal(new[] { 0, 4 }, loaded.Slots.Select(s => s.SlotId));
        Assert.Equal("0x51", loaded.Slots[1].I2cAddress);
        // Topology is observed, not assigned.
        Assert.All(loaded.Slots, slot => Assert.Null(slot.Role));
    }

    [Fact]
    public async Task RecordHeartbeatAsync_updates_the_existing_row_rather_than_inserting()
    {
        using var db = new SqliteTestDatabase();
        var repository = new EdgeUnitRepository(db.Database);

        await repository.RecordHeartbeatAsync(Heartbeat((0, "0x25")), SeenAt);
        var second = await repository.RecordHeartbeatAsync(Heartbeat((0, "0x25")), SeenAt.AddMinutes(1));

        Assert.False(second.Registered);
        Assert.Equal(1, db.CountRows("EdgeUnits"));
        var loaded = await repository.GetAsync(DeviceId);
        Assert.Equal(SeenAt.AddMinutes(1), loaded!.LastHeartbeatAt);
        // First-seen is set once and never moves.
        Assert.Equal(SeenAt, loaded.FirstSeenAt);
    }

    [Fact]
    public async Task RecordHeartbeatAsync_removes_slots_that_are_no_longer_reported()
    {
        using var db = new SqliteTestDatabase();
        var repository = new EdgeUnitRepository(db.Database);

        await repository.RecordHeartbeatAsync(Heartbeat((0, "0x25"), (4, "0x51")), SeenAt);
        await repository.RecordHeartbeatAsync(Heartbeat((0, "0x25")), SeenAt.AddMinutes(1));

        var loaded = await repository.GetAsync(DeviceId);
        Assert.Equal(0, Assert.Single(loaded!.Slots).SlotId);
        Assert.Equal(1, db.CountRows("SlotTopologies"));
    }

    [Fact]
    public async Task GetAllAsync_returns_every_registered_unit()
    {
        using var db = new SqliteTestDatabase();
        var repository = new EdgeUnitRepository(db.Database);

        await repository.RecordHeartbeatAsync(Heartbeat("2BEEF0000001", (0, "0x25")), SeenAt);
        await repository.RecordHeartbeatAsync(Heartbeat(DeviceId, (0, "0x25")), SeenAt);

        var all = await repository.GetAllAsync();

        Assert.Equal(new[] { DeviceId, "2BEEF0000001" }, all.Select(u => u.DeviceId));
    }

    [Fact]
    public async Task UpdateMappingAsync_increments_the_version_and_assigns_slots()
    {
        using var db = new SqliteTestDatabase();
        var repository = new EdgeUnitRepository(db.Database);
        await repository.RecordHeartbeatAsync(Heartbeat((0, "0x25"), (4, "0x51")), SeenAt);

        var updated = await repository.UpdateMappingAsync(
            DeviceId,
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
    public async Task RecordHeartbeatAsync_raises_the_drift_flag_for_a_mapped_unit()
    {
        using var db = new SqliteTestDatabase();
        var repository = new EdgeUnitRepository(db.Database);
        await repository.RecordHeartbeatAsync(Heartbeat((0, "0x25")), SeenAt);
        await repository.UpdateMappingAsync(DeviceId, Mapping());

        var drifted = await repository.RecordHeartbeatAsync(Heartbeat((0, "0x26")), SeenAt.AddMinutes(1));

        Assert.True(drifted.DriftNewlyDetected);
        var loaded = await repository.GetAsync(DeviceId);
        Assert.Equal(SeenAt.AddMinutes(1), loaded!.TopologyDriftDetectedAt);
        // The acknowledged mapping stays active until a replacement is acknowledged.
        Assert.Equal(1, loaded.MappingVersion);
        Assert.Equal("East Sensor Unit", loaded.UnitName);
    }

    [Fact]
    public async Task UpdateMappingStatusAsync_clears_the_drift_flag_only_when_asked()
    {
        using var db = new SqliteTestDatabase();
        var repository = new EdgeUnitRepository(db.Database);
        await repository.RecordHeartbeatAsync(Heartbeat((0, "0x25")), SeenAt);
        await repository.UpdateMappingAsync(DeviceId, Mapping());
        await repository.RecordHeartbeatAsync(Heartbeat((0, "0x26")), SeenAt.AddMinutes(1));

        await repository.UpdateMappingStatusAsync(DeviceId, MappingStatuses.Published, clearTopologyDrift: false);
        var afterPublish = await repository.GetAsync(DeviceId);
        Assert.Equal(MappingStatuses.Published, afterPublish!.MappingStatus);
        Assert.True(afterPublish.HasTopologyDrift);

        await repository.UpdateMappingStatusAsync(DeviceId, MappingStatuses.Acknowledged, clearTopologyDrift: true);
        var afterAck = await repository.GetAsync(DeviceId);
        Assert.Equal(MappingStatuses.Acknowledged, afterAck!.MappingStatus);
        Assert.False(afterAck.HasTopologyDrift);
    }

    [Fact]
    public async Task RecordHeartbeatAsync_never_reverts_an_accepted_mapping()
    {
        using var db = new SqliteTestDatabase();
        var repository = new EdgeUnitRepository(db.Database);
        await repository.RecordHeartbeatAsync(Heartbeat((0, "0x25")), SeenAt);
        await repository.UpdateMappingAsync(DeviceId, Mapping());

        // Same topology as the mapping was built against: pure liveness.
        await repository.RecordHeartbeatAsync(Heartbeat((0, "0x25")), SeenAt.AddMinutes(1));

        var loaded = await repository.GetAsync(DeviceId);
        Assert.Equal(1, loaded!.MappingVersion);
        Assert.Equal(MappingStatuses.PublishPending, loaded.MappingStatus);
        Assert.Equal("East Sensor Unit", loaded.UnitName);
        Assert.Equal("Zone A", loaded.Location);
        Assert.Equal(SlotRoles.Sensor, loaded.Slots[0].Role);
        Assert.Equal("Bed A Moisture", loaded.Slots[0].Label);
    }

    [Fact]
    public async Task A_heartbeat_concurrent_with_a_mapping_update_cannot_lose_the_mapping()
    {
        using var db = new SqliteTestDatabase();
        var repository = new EdgeUnitRepository(db.Database);
        await repository.RecordHeartbeatAsync(Heartbeat((0, "0x25")), SeenAt);

        // The onboarding hot path: the operator submits the mapping in response to
        // mapping-required, which the first heartbeat just raised, while heartbeats keep coming.
        // Either order is legal; silently reverting the mapping is not.
        await Task.WhenAll(
            repository.RecordHeartbeatAsync(Heartbeat((0, "0x25")), SeenAt.AddMinutes(1)),
            repository.UpdateMappingAsync(DeviceId, Mapping()));

        var loaded = await repository.GetAsync(DeviceId);
        Assert.Equal(1, loaded!.MappingVersion);
        Assert.Equal("East Sensor Unit", loaded.UnitName);
        Assert.Equal("Zone A", loaded.Location);
        Assert.Equal(SlotRoles.Sensor, loaded.Slots[0].Role);
        Assert.Equal(MappingStatuses.PublishPending, loaded.MappingStatus);
    }
}
