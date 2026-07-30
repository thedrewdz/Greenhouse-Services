using Microsoft.AspNetCore.SignalR;

namespace Greenhouse.Api.Hubs;

/// <summary>
/// The onboarding observation channel at <c>/hubs/onboarding</c>. It is server-to-client only:
/// the UI subscribes on startup and receives <c>DeviceDiscovered</c> and
/// <c>OnboardingStateChanged</c> events, and drives the workflow through the REST resources.
/// </summary>
/// <remarks>
/// Deliberately has no hub methods. Backend state is authoritative and always readable from
/// <c>GET /api/onboarding</c>, which is the documented fallback when SignalR is unavailable.
/// </remarks>
public sealed class OnboardingHub : Hub
{
    /// <summary>The route the host maps this hub on.</summary>
    public const string Path = "/hubs/onboarding";
}
