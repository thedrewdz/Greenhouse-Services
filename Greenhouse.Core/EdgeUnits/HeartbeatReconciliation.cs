namespace Greenhouse.Core.EdgeUnits;

/// <summary>
/// The heartbeat rules: what a reported heartbeat means for a unit's registration, liveness, and
/// Drift Flag. A pure function of the stored unit and the reported payload, so the decision can be
/// made inside the same storage operation that writes it.
/// </summary>
/// <remarks>
/// Deliberately pure and stateless. Heartbeat ingestion must be a single read-modify-write: the
/// operator submits a mapping in response to <c>mapping-required</c> — which the first heartbeat
/// just raised — while heartbeats keep arriving, so a decision made against a snapshot read in an
/// earlier operation can be stale by the time it is written and would silently revert the mapping.
/// </remarks>
public static class HeartbeatReconciliation
{
    /// <summary>Advertised-name convention every Edge Unit follows.</summary>
    public const string AdvertisedNamePrefix = "GH-Edge-";

    /// <summary>
    /// Returns the unit as it should be stored after <paramref name="heartbeat"/>, given
    /// <paramref name="existing"/> (<c>null</c> when the unit is not yet registered).
    /// </summary>
    public static HeartbeatOutcome Reconcile(EdgeUnit? existing, HeartbeatMessage heartbeat, DateTime receivedAt)
    {
        if (existing is null)
        {
            return new HeartbeatOutcome(Register(heartbeat, receivedAt), Registered: true, DriftNewlyDetected: false);
        }

        // A bootstrap heartbeat may omit topology entirely; that is liveness, not a change.
        if (heartbeat.Slots is null || TopologyMatches(existing.Slots, heartbeat.Slots))
        {
            return new HeartbeatOutcome(
                existing with { LastHeartbeatAt = receivedAt },
                Registered: false,
                DriftNewlyDetected: false);
        }

        // Topology changed. Carry the acknowledged assignments across for slots whose module is
        // unchanged, so the previously acknowledged mapping stays active for them; slots that
        // appeared or changed module come back unassigned and need operator input.
        var slots = heartbeat.Slots
            .Select(slot =>
            {
                var previous = existing.Slots.FirstOrDefault(
                    p => p.SlotId == slot.SlotId
                         && string.Equals(p.I2cAddress, slot.I2cAddress, StringComparison.OrdinalIgnoreCase));

                return new EdgeUnitSlot(
                    slot.SlotId,
                    slot.I2cAddress,
                    previous?.Role,
                    previous?.Capability,
                    previous?.Label,
                    receivedAt);
            })
            .ToArray();

        // Drift only applies to a unit that already has a mapping to diverge from, and the flag
        // keeps its original detection time until an acknowledgement clears it.
        var isMapped = existing.MappingVersion > 0;
        DateTime? driftDetectedAt = isMapped
            ? existing.TopologyDriftDetectedAt ?? receivedAt
            : null;

        return new HeartbeatOutcome(
            existing with
            {
                LastHeartbeatAt = receivedAt,
                TopologyDriftDetectedAt = driftDetectedAt,
                Slots = slots,
            },
            Registered: false,
            DriftNewlyDetected: isMapped && existing.TopologyDriftDetectedAt is null);
    }

    private static EdgeUnit Register(HeartbeatMessage heartbeat, DateTime receivedAt) => new(
        heartbeat.DeviceId,
        AdvertisedNamePrefix + heartbeat.DeviceId,
        UnitName: null,
        Location: null,
        MappingVersion: 0,
        MappingStatus: MappingStatuses.PendingMapping,
        FirstSeenAt: receivedAt,
        LastHeartbeatAt: receivedAt,
        TopologyDriftDetectedAt: null,
        Slots: heartbeat.Slots is null
            ? Array.Empty<EdgeUnitSlot>()
            : heartbeat.Slots
                .Select(slot => new EdgeUnitSlot(
                    slot.SlotId,
                    slot.I2cAddress,
                    Role: null,
                    Capability: null,
                    Label: null,
                    receivedAt))
                .ToArray());

    /// <summary>
    /// Topology is the set of slot ids and the module identity (I2C address) on each. Capability
    /// and telemetry values change constantly and are not part of the comparison.
    /// </summary>
    private static bool TopologyMatches(IReadOnlyList<EdgeUnitSlot> stored, IReadOnlyList<HeartbeatSlot> reported)
    {
        if (stored.Count != reported.Count)
        {
            return false;
        }

        // Compare distinct slot ids, not just the count: a payload repeating a slot id would
        // otherwise match a smaller stored topology of the same length.
        var reportedBySlot = new Dictionary<int, string>();
        foreach (var slot in reported)
        {
            if (!reportedBySlot.TryAdd(slot.SlotId, slot.I2cAddress))
            {
                return false;
            }
        }

        if (reportedBySlot.Count != stored.Count)
        {
            return false;
        }

        foreach (var slot in stored)
        {
            if (!reportedBySlot.TryGetValue(slot.SlotId, out var address)
                || !string.Equals(slot.I2cAddress, address, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>
/// The result of reconciling a heartbeat: the unit as it should now be stored, plus the two facts
/// a caller needs to react to.
/// </summary>
/// <param name="Unit">The unit to persist.</param>
/// <param name="Registered">True when this heartbeat registered a previously unknown unit.</param>
/// <param name="DriftNewlyDetected">
/// True only on the heartbeat that first raised the Drift Flag, so the divergence is logged once
/// rather than on every subsequent heartbeat.
/// </param>
public sealed record HeartbeatOutcome(EdgeUnit Unit, bool Registered, bool DriftNewlyDetected);
