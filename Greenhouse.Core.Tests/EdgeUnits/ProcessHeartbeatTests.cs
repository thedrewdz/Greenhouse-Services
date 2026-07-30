using Greenhouse.Core.EdgeUnits;
using Greenhouse.Core.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Greenhouse.Core.Tests.EdgeUnits;

public class ProcessHeartbeatTests
{
    private const string DeviceId = "1ADD5912AF61";
    private static readonly DateTime FirstSeen = new(2026, 7, 1, 22, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 7, 1, 22, 5, 0, DateTimeKind.Utc);

    private static (ProcessHeartbeat Handler, FakeEdgeUnitRepository Units, RecordingOnboardingWorkflow Onboarding)
        Create(EdgeUnit? seed = null)
    {
        var units = new FakeEdgeUnitRepository();
        if (seed is not null)
        {
            units.Units[seed.DeviceId] = seed;
        }

        var onboarding = new RecordingOnboardingWorkflow();
        return (new ProcessHeartbeat(units, onboarding, NullLogger<ProcessHeartbeat>.Instance), units, onboarding);
    }

    private static MessageEnvelope Envelope(string payload) =>
        new(EdgeUnitTopics.Heartbeat, payload, Now);

    private static string Heartbeat(params (int SlotId, string Address)[] slots)
    {
        var slotJson = string.Join(
            ",",
            slots.Select(s =>
                $"{{\"slot_id\":{s.SlotId},\"direction\":\"sensor\",\"i2c_address\":\"{s.Address}\"," +
                "\"capability\":\"moisture\",\"state\":\"none\",\"value\":41.7,\"error_code\":0}"));

        return $"{{\"id\":8339,\"device_id\":\"{DeviceId}\",\"slot_count\":{slots.Length},\"slots\":[{slotJson}]}}";
    }

    private static EdgeUnit Mapped(int mappingVersion = 1, params (int SlotId, string Address)[] slots) => new(
        DeviceId,
        "GH-Edge-" + DeviceId,
        "East Sensor Unit",
        "Zone A",
        mappingVersion,
        MappingStatuses.Acknowledged,
        FirstSeen,
        FirstSeen,
        TopologyDriftDetectedAt: null,
        Slots: slots
            .Select(s => new EdgeUnitSlot(s.SlotId, s.Address, SlotRoles.Sensor, "moisture", "Bed A", FirstSeen))
            .ToArray());

    [Fact]
    public async Task An_unknown_device_is_registered_pending_mapping()
    {
        var (handler, units, _) = Create();

        await handler.HandleAsync(Envelope(Heartbeat((0, "0x25"), (4, "0x51"))));

        var registered = units.Units[DeviceId];
        Assert.Equal("GH-Edge-" + DeviceId, registered.AdvertisedName);
        Assert.Equal(MappingStatuses.PendingMapping, registered.MappingStatus);
        Assert.Equal(0, registered.MappingVersion);
        Assert.Equal(Now, registered.FirstSeenAt);
        Assert.Equal(Now, registered.LastHeartbeatAt);
        Assert.Equal(new[] { 0, 4 }, registered.Slots.Select(s => s.SlotId));
        // Topology is observed, not assigned: nothing is mapped until the operator submits one.
        Assert.All(registered.Slots, slot => Assert.Null(slot.Role));
    }

    [Fact]
    public async Task Every_heartbeat_offers_completion_to_the_onboarding_session()
    {
        var (handler, _, onboarding) = Create();

        await handler.HandleAsync(Envelope(Heartbeat((0, "0x25"))));

        Assert.Equal(DeviceId, Assert.Single(onboarding.Completed));
    }

    [Fact]
    public async Task A_bootstrap_heartbeat_without_topology_still_registers_the_device()
    {
        var (handler, units, onboarding) = Create();

        await handler.HandleAsync(Envelope($"{{\"id\":1,\"device_id\":\"{DeviceId}\"}}"));

        Assert.Empty(units.Units[DeviceId].Slots);
        Assert.Equal(DeviceId, Assert.Single(onboarding.Completed));
    }

    [Fact]
    public async Task A_bootstrap_heartbeat_never_erases_a_known_units_topology()
    {
        var (handler, units, _) = Create(Mapped(slots: (0, "0x25")));

        await handler.HandleAsync(Envelope($"{{\"id\":2,\"device_id\":\"{DeviceId}\"}}"));

        var unit = units.Units[DeviceId];
        Assert.Equal(Now, unit.LastHeartbeatAt);
        Assert.Single(unit.Slots);
        Assert.False(unit.HasTopologyDrift);
    }

    [Fact]
    public async Task Matching_topology_updates_only_the_heartbeat_timestamp()
    {
        var (handler, units, _) = Create(Mapped(slots: new[] { (0, "0x25"), (4, "0x51") }));

        await handler.HandleAsync(Envelope(Heartbeat((0, "0x25"), (4, "0x51"))));

        var unit = units.Units[DeviceId];
        Assert.Equal(Now, unit.LastHeartbeatAt);
        Assert.False(unit.HasTopologyDrift);
        Assert.Equal(MappingStatuses.Acknowledged, unit.MappingStatus);
        Assert.Equal("Bed A", unit.Slots[0].Label);
    }

    [Fact]
    public async Task Slot_order_in_the_payload_does_not_count_as_drift()
    {
        var (handler, units, _) = Create(Mapped(slots: new[] { (0, "0x25"), (4, "0x51") }));

        await handler.HandleAsync(Envelope(Heartbeat((4, "0x51"), (0, "0x25"))));

        Assert.False(units.Units[DeviceId].HasTopologyDrift);
    }

    [Fact]
    public async Task A_changed_module_raises_the_drift_flag()
    {
        var (handler, units, _) = Create(Mapped(slots: (0, "0x25")));

        await handler.HandleAsync(Envelope(Heartbeat((0, "0x26"))));

        var unit = units.Units[DeviceId];
        Assert.True(unit.HasTopologyDrift);
        Assert.Equal(Now, unit.TopologyDriftDetectedAt);
        // The acknowledged mapping stays active until a replacement is acknowledged.
        Assert.Equal(MappingStatuses.Acknowledged, unit.MappingStatus);
        Assert.Equal(1, unit.MappingVersion);
    }

    [Fact]
    public async Task An_added_slot_raises_the_drift_flag_and_keeps_unchanged_assignments()
    {
        var (handler, units, _) = Create(Mapped(slots: (0, "0x25")));

        await handler.HandleAsync(Envelope(Heartbeat((0, "0x25"), (4, "0x51"))));

        var unit = units.Units[DeviceId];
        Assert.True(unit.HasTopologyDrift);
        Assert.Equal("moisture", unit.Slots[0].Capability);
        // The new slot arrives unassigned; it needs operator input before it can be commanded.
        Assert.Null(unit.Slots[1].Role);
        Assert.Null(unit.Slots[1].Capability);
    }

    [Fact]
    public async Task The_drift_flag_keeps_its_original_detection_time_across_heartbeats()
    {
        var (handler, units, _) = Create(Mapped(slots: (0, "0x25")));

        await handler.HandleAsync(Envelope(Heartbeat((0, "0x26"))));
        await handler.HandleAsync(new MessageEnvelope(
            EdgeUnitTopics.Heartbeat,
            Heartbeat((0, "0x26")),
            Now.AddMinutes(1)));

        Assert.Equal(Now, units.Units[DeviceId].TopologyDriftDetectedAt);
    }

    [Fact]
    public async Task An_unmapped_unit_refreshes_topology_without_raising_drift()
    {
        // Nothing has been acknowledged yet, so there is no mapping for the topology to diverge from.
        var seed = Mapped(mappingVersion: 0, slots: (0, "0x25")) with
        {
            MappingStatus = MappingStatuses.PendingMapping,
        };
        var (handler, units, _) = Create(seed);

        await handler.HandleAsync(Envelope(Heartbeat((0, "0x25"), (4, "0x51"))));

        var unit = units.Units[DeviceId];
        Assert.False(unit.HasTopologyDrift);
        Assert.Equal(2, unit.Slots.Count);
    }

    [Fact]
    public async Task A_liveness_heartbeat_never_touches_the_mapping()
    {
        var mapped = Mapped(slots: (0, "0x25"));
        var (handler, units, _) = Create(mapped);

        await handler.HandleAsync(Envelope(Heartbeat((0, "0x25"))));

        var unit = units.Units[DeviceId];
        Assert.Equal(mapped.MappingVersion, unit.MappingVersion);
        Assert.Equal(mapped.MappingStatus, unit.MappingStatus);
        Assert.Equal(mapped.UnitName, unit.UnitName);
        Assert.Equal(mapped.Location, unit.Location);
        Assert.Equal(mapped.Slots[0].Role, unit.Slots[0].Role);
        Assert.Equal(mapped.Slots[0].Label, unit.Slots[0].Label);
    }

    [Fact]
    public async Task A_repeated_slot_id_is_treated_as_drift_rather_than_a_match()
    {
        // Same slot count as the stored topology, but only one distinct slot: a firmware bug
        // must not read as "topology unchanged".
        var (handler, units, _) = Create(Mapped(slots: new[] { (0, "0x25"), (4, "0x51") }));

        await handler.HandleAsync(Envelope(Heartbeat((0, "0x25"), (0, "0x25"))));

        Assert.True(units.Units[DeviceId].HasTopologyDrift);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"id\":1}")]
    [InlineData("{\"id\":1,\"device_id\":\"\"}")]
    public async Task A_malformed_heartbeat_is_discarded_rather_than_thrown(string payload)
    {
        var (handler, units, onboarding) = Create();

        await handler.HandleAsync(Envelope(payload));

        Assert.Empty(units.Units);
        Assert.Empty(onboarding.Completed);
    }
}
