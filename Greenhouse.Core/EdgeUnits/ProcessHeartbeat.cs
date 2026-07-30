using Greenhouse.Core.Messaging;
using Greenhouse.Core.Onboarding;
using Microsoft.Extensions.Logging;

namespace Greenhouse.Core.EdgeUnits;

/// <summary>
/// Routes every <c>gh/heartbeat</c> message: registers unknown Edge Units, refreshes liveness for
/// known ones, and raises the Drift Flag when reported topology diverges from the mapping that
/// was acknowledged.
/// </summary>
/// <remarks>
/// This is the single place heartbeat semantics live. It is a cross-cutting message handler, not
/// an onboarding-specific service — onboarding merely observes it, by being told when the device
/// it is waiting on has reported in.
/// </remarks>
public sealed class ProcessHeartbeat
{
    private const string AdvertisedNamePrefix = "GH-Edge-";

    private readonly IEdgeUnitRepository _edgeUnits;
    private readonly IOnboardingWorkflow _onboarding;
    private readonly ILogger<ProcessHeartbeat> _logger;

    public ProcessHeartbeat(
        IEdgeUnitRepository edgeUnits,
        IOnboardingWorkflow onboarding,
        ILogger<ProcessHeartbeat> logger)
    {
        _edgeUnits = edgeUnits;
        _onboarding = onboarding;
        _logger = logger;
    }

    public async Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        var heartbeat = HeartbeatMessage.TryParse(envelope.Payload);
        if (heartbeat is null)
        {
            _logger.LogWarning("Discarded malformed heartbeat on '{Topic}'.", envelope.Topic);
            return;
        }

        var existing = await _edgeUnits.GetAsync(heartbeat.DeviceId, cancellationToken);

        if (existing is null)
        {
            await RegisterAsync(heartbeat, envelope.ReceivedAt, cancellationToken);
        }
        else
        {
            await RefreshAsync(existing, heartbeat, envelope.ReceivedAt, cancellationToken);
        }

        // A no-op unless an onboarding session is waiting on exactly this device's first
        // heartbeat; the workflow owns that decision.
        await _onboarding.CompleteOnboardingAsync(heartbeat.DeviceId, cancellationToken);
    }

    private Task RegisterAsync(HeartbeatMessage heartbeat, DateTime receivedAt, CancellationToken cancellationToken)
    {
        var registered = new EdgeUnit(
            heartbeat.DeviceId,
            AdvertisedNamePrefix + heartbeat.DeviceId,
            UnitName: null,
            Location: null,
            MappingVersion: 0,
            MappingStatus: MappingStatuses.PendingMapping,
            FirstSeenAt: receivedAt,
            LastHeartbeatAt: receivedAt,
            TopologyDriftDetectedAt: null,
            Slots: ObservedSlots(heartbeat, receivedAt));

        return _edgeUnits.UpsertAsync(registered, cancellationToken);
    }

    private async Task RefreshAsync(
        EdgeUnit existing,
        HeartbeatMessage heartbeat,
        DateTime receivedAt,
        CancellationToken cancellationToken)
    {
        // A bootstrap heartbeat may omit topology entirely; that is liveness, not a change.
        if (heartbeat.Slots is null)
        {
            await _edgeUnits.UpsertAsync(existing with { LastHeartbeatAt = receivedAt }, cancellationToken);
            return;
        }

        if (TopologyMatches(existing.Slots, heartbeat.Slots))
        {
            await _edgeUnits.UpsertAsync(existing with { LastHeartbeatAt = receivedAt }, cancellationToken);
            return;
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

        if (isMapped && existing.TopologyDriftDetectedAt is null)
        {
            _logger.LogWarning(
                "Edge Unit '{DeviceId}' reported a slot topology that differs from mapping version {MappingVersion}; reconfiguration is required.",
                existing.DeviceId,
                existing.MappingVersion);
        }

        await _edgeUnits.UpsertAsync(
            existing with
            {
                LastHeartbeatAt = receivedAt,
                TopologyDriftDetectedAt = driftDetectedAt,
                Slots = slots,
            },
            cancellationToken);
    }

    private static IReadOnlyList<EdgeUnitSlot> ObservedSlots(HeartbeatMessage heartbeat, DateTime receivedAt) =>
        heartbeat.Slots is null
            ? Array.Empty<EdgeUnitSlot>()
            : heartbeat.Slots
                .Select(slot => new EdgeUnitSlot(
                    slot.SlotId,
                    slot.I2cAddress,
                    Role: null,
                    Capability: null,
                    Label: null,
                    receivedAt))
                .ToArray();

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

        var storedBySlot = stored.ToDictionary(slot => slot.SlotId);

        foreach (var slot in reported)
        {
            if (!storedBySlot.TryGetValue(slot.SlotId, out var match)
                || !string.Equals(match.I2cAddress, slot.I2cAddress, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}
