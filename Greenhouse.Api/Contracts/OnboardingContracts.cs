using Greenhouse.Core.Onboarding;

namespace Greenhouse.Api.Contracts;

/// <summary>A discovered onboarding candidate, in both the REST and hub payloads.</summary>
public sealed record OnboardingCandidateResponse(string DeviceId, string AdvertisedName, int? Rssi)
{
    public static OnboardingCandidateResponse From(ProvisionableUnit unit) =>
        new(unit.DeviceId, unit.AdvertisedName, unit.Rssi);
}

/// <summary>Response for <c>GET /api/onboarding</c> — the polling fallback for the hub.</summary>
public sealed record OnboardingStateResponse(
    string Status,
    IReadOnlyList<OnboardingCandidateResponse> Candidates,
    string? SelectedDeviceId,
    int? ErrorCode,
    string? ErrorMessage)
{
    public static OnboardingStateResponse From(OnboardingState state) => new(
        state.Status,
        state.Candidates.Select(OnboardingCandidateResponse.From).ToArray(),
        state.SelectedDeviceId,
        state.ErrorCode,
        state.ErrorMessage);
}

/// <summary>Response for <c>POST /api/onboarding/scan</c>.</summary>
public sealed record OnboardingScanResponse(string Status);

/// <summary>Response for <c>POST /api/onboarding/{device_id}/start</c>.</summary>
public sealed record OnboardingStartResponse(string Status, string DeviceId);

/// <summary>Response for <c>POST /api/onboarding/{device_id}/cancel</c>.</summary>
public sealed record OnboardingCancelResponse(string Status);

/// <summary>Payload of the <c>OnboardingStateChanged</c> hub event.</summary>
public sealed record OnboardingStateChangedEvent(
    string Status,
    string? SelectedDeviceId,
    int? ErrorCode,
    string? ErrorMessage);
