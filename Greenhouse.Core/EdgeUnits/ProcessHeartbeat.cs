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

        // Registration, liveness, and drift are decided and written in one atomic operation; a
        // mapping accepted concurrently must never be reverted by a stale heartbeat snapshot.
        var outcome = await _edgeUnits.RecordHeartbeatAsync(heartbeat, envelope.ReceivedAt, cancellationToken);

        if (outcome.DriftNewlyDetected)
        {
            _logger.LogWarning(
                "Edge Unit '{DeviceId}' reported a slot topology that differs from mapping version {MappingVersion}; reconfiguration is required.",
                outcome.Unit.DeviceId,
                outcome.Unit.MappingVersion);
        }

        // A no-op unless an onboarding session is waiting on exactly this device's first
        // heartbeat; the workflow owns that decision.
        await _onboarding.CompleteOnboardingAsync(heartbeat.DeviceId, cancellationToken);
    }
}
