namespace Greenhouse.Core.Onboarding;

/// <summary>
/// Outbound port for the live onboarding observation channel. The transport adapter (the
/// SignalR hub at <c>/hubs/onboarding</c>) implements it; application code never references
/// SignalR types.
/// </summary>
/// <remarks>
/// This channel is observation only — backend state stays authoritative and is always readable
/// through <c>GET /api/onboarding</c>. Notification failures must therefore never fail or stall
/// the workflow that raised them.
/// </remarks>
public interface IOnboardingNotifier
{
    /// <summary>Raises <c>DeviceDiscovered</c> for a newly found candidate.</summary>
    Task DeviceDiscoveredAsync(ProvisionableUnit candidate, CancellationToken cancellationToken = default);

    /// <summary>Raises <c>OnboardingStateChanged</c> for a workflow state transition.</summary>
    Task StateChangedAsync(OnboardingStateChange change, CancellationToken cancellationToken = default);
}
