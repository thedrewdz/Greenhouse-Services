namespace Greenhouse.Core.Onboarding;

/// <summary>
/// The complete backend-owned onboarding state. The backend is authoritative: the UI reads this
/// (via <c>GET /api/onboarding</c> or the hub) and never keeps workflow state of its own.
/// </summary>
/// <param name="Status">One of <see cref="OnboardingStatuses"/>.</param>
/// <param name="Candidates">Currently discovered candidates; empty unless a scan has run.</param>
/// <param name="SelectedDeviceId">Device the operator selected, once one has been selected.</param>
/// <param name="ErrorCode">Canonical onboarding error code; non-null only when failed.</param>
/// <param name="ErrorMessage">Short diagnostic; non-null only when failed.</param>
public sealed record OnboardingState(
    string Status,
    IReadOnlyList<ProvisionableUnit> Candidates,
    string? SelectedDeviceId,
    int? ErrorCode,
    string? ErrorMessage)
{
    /// <summary>The state of a Main Unit with no onboarding session in progress.</summary>
    public static OnboardingState Idle { get; } = new(
        OnboardingStatuses.Idle,
        Array.Empty<ProvisionableUnit>(),
        SelectedDeviceId: null,
        ErrorCode: null,
        ErrorMessage: null);
}

/// <summary>
/// The <c>OnboardingStateChanged</c> hub event payload — the state without the candidate list,
/// which arrives incrementally as <c>DeviceDiscovered</c> events instead.
/// </summary>
public sealed record OnboardingStateChange(
    string Status,
    string? SelectedDeviceId,
    int? ErrorCode,
    string? ErrorMessage)
{
    public static OnboardingStateChange From(OnboardingState state) =>
        new(state.Status, state.SelectedDeviceId, state.ErrorCode, state.ErrorMessage);
}
