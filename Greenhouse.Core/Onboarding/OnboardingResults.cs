namespace Greenhouse.Core.Onboarding;

/// <summary>
/// Outcome of a request to start a scan. A closed hierarchy: exactly one of
/// <see cref="Started"/> or <see cref="SessionActive"/>.
/// </summary>
public abstract record StartScanResult
{
    private StartScanResult()
    {
    }

    /// <summary>Scanning began; state is <c>scanning</c>.</summary>
    public sealed record Started(OnboardingState State) : StartScanResult;

    /// <summary>A session is already in progress; the caller must cancel it first.</summary>
    public sealed record SessionActive(OnboardingState State) : StartScanResult;
}

/// <summary>
/// Outcome of selecting a candidate for provisioning. A closed hierarchy: exactly one of
/// <see cref="Accepted"/>, <see cref="UnknownCandidate"/>, or <see cref="DifferentDeviceSelected"/>.
/// </summary>
public abstract record SelectDeviceResult
{
    private SelectDeviceResult()
    {
    }

    /// <summary>
    /// Provisioning was started, or was already running for this device — repeating the request
    /// returns the current state rather than duplicating BLE work.
    /// </summary>
    public sealed record Accepted(OnboardingState State) : SelectDeviceResult;

    /// <summary>The device is not among the current candidates.</summary>
    public sealed record UnknownCandidate : SelectDeviceResult;

    /// <summary>A different device is already selected for this session.</summary>
    public sealed record DifferentDeviceSelected(OnboardingState State) : SelectDeviceResult;
}
