namespace Greenhouse.Core.Onboarding;

/// <summary>
/// The backend-owned Edge Unit onboarding workflow. It holds the single active session, drives
/// BLE scanning and auto-provisioning in the background, and publishes every transition to the
/// observation channel.
/// </summary>
/// <remarks>
/// The four operations are the onboarding use cases: start a scan, select and provision a
/// candidate, cancel, and complete on first heartbeat. They are grouped behind one contract
/// because they share a single session's lifetime — the scan and provisioning tasks outlive the
/// request that started them, and only one owner of that state can keep it consistent.
/// </remarks>
public interface IOnboardingWorkflow
{
    /// <summary>Returns the current backend-owned state. Never depends on UI-held context.</summary>
    Task<OnboardingState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts BLE scanning when no session is active.</summary>
    Task<StartScanResult> StartOnboardingScanAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops scanning, selects <paramref name="deviceId"/>, and starts auto-provisioning it with
    /// stored WiFi credentials and a broker URI derived from the Main Unit's local address.
    /// </summary>
    Task<SelectDeviceResult> SelectAndProvisionEdgeUnitAsync(
        string deviceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels the session for <paramref name="deviceId"/> and returns to idle. Cancelling a
    /// session that is already idle is a no-op that returns the idle state.
    /// </summary>
    Task<OnboardingState> CancelOnboardingAsync(
        string deviceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the session as needing runtime mapping after the first valid heartbeat arrives from
    /// the selected device. Ignored when no session is awaiting that device's heartbeat.
    /// </summary>
    Task CompleteOnboardingAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the session complete once the selected device's mapping has been stored and
    /// published. Ignored when the session is not waiting on that device's mapping.
    /// </summary>
    Task CompleteMappingAsync(string deviceId, CancellationToken cancellationToken = default);
}
