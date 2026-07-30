using Greenhouse.Api.Contracts;
using Greenhouse.Core.Onboarding;
using Microsoft.AspNetCore.SignalR;

namespace Greenhouse.Api.Hubs;

/// <summary>
/// SignalR adapter for <see cref="IOnboardingNotifier"/>. It is the only place that knows the
/// hub event names and their payload shapes; the workflow that raises them stays free of
/// transport types.
/// </summary>
public sealed class SignalROnboardingNotifier : IOnboardingNotifier
{
    /// <summary>Event names are part of the published hub contract — do not rename.</summary>
    private const string DeviceDiscoveredEvent = "DeviceDiscovered";

    private const string OnboardingStateChangedEvent = "OnboardingStateChanged";

    private readonly IHubContext<OnboardingHub> _hub;

    public SignalROnboardingNotifier(IHubContext<OnboardingHub> hub)
    {
        _hub = hub;
    }

    public Task DeviceDiscoveredAsync(ProvisionableUnit candidate, CancellationToken cancellationToken = default) =>
        _hub.Clients.All.SendAsync(
            DeviceDiscoveredEvent,
            OnboardingCandidateResponse.From(candidate),
            cancellationToken);

    public Task StateChangedAsync(OnboardingStateChange change, CancellationToken cancellationToken = default) =>
        _hub.Clients.All.SendAsync(
            OnboardingStateChangedEvent,
            new OnboardingStateChangedEvent(
                change.Status,
                change.SelectedDeviceId,
                change.ErrorCode,
                change.ErrorMessage),
            cancellationToken);
}
