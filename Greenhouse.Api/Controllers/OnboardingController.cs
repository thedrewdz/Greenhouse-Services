using Greenhouse.Api.Contracts;
using Greenhouse.Core.Onboarding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Greenhouse.Api.Controllers;

/// <summary>
/// The onboarding workflow resources. Every call is stateless — the path, action, and body carry
/// the whole request context — and the backend state they return is authoritative, so a UI that
/// refreshes or navigates away recovers by reading it again.
/// </summary>
[ApiController]
[Route("api/onboarding")]
public sealed class OnboardingController : ControllerBase
{
    private readonly IOnboardingWorkflow _workflow;

    public OnboardingController(IOnboardingWorkflow workflow)
    {
        _workflow = workflow;
    }

    /// <summary>Starts a BLE scan. Returns 409 when a session is already active.</summary>
    [HttpPost("scan")]
    [ProducesResponseType(typeof(OnboardingScanResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(OnboardingStateResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Scan(CancellationToken cancellationToken)
    {
        var result = await _workflow.StartOnboardingScanAsync(cancellationToken);

        return result switch
        {
            StartScanResult.Started started =>
                Accepted(new OnboardingScanResponse(started.State.Status)),
            StartScanResult.SessionActive active =>
                Conflict(OnboardingStateResponse.From(active.State)),
            _ => throw new InvalidOperationException("Unexpected scan result."),
        };
    }

    /// <summary>Returns the current onboarding state. Polling fallback when SignalR is unavailable.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(OnboardingStateResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OnboardingStateResponse>> Get(CancellationToken cancellationToken)
    {
        var state = await _workflow.GetStateAsync(cancellationToken);
        return Ok(OnboardingStateResponse.From(state));
    }

    /// <summary>
    /// Selects a discovered candidate and starts auto-provisioning it. Repeating the call for the
    /// device already being provisioned returns the current state instead of repeating BLE work.
    /// </summary>
    [HttpPost("{deviceId}/start")]
    [ProducesResponseType(typeof(OnboardingStartResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(OnboardingStateResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start(string deviceId, CancellationToken cancellationToken)
    {
        var result = await _workflow.SelectAndProvisionEdgeUnitAsync(deviceId, cancellationToken);

        return result switch
        {
            SelectDeviceResult.Accepted accepted =>
                Accepted(new OnboardingStartResponse(accepted.State.Status, deviceId)),
            SelectDeviceResult.UnknownCandidate => NotFound(),
            SelectDeviceResult.DifferentDeviceSelected conflict =>
                Conflict(OnboardingStateResponse.From(conflict.State)),
            _ => throw new InvalidOperationException("Unexpected device selection result."),
        };
    }

    /// <summary>Cancels the session and returns to idle. Repeating the call is a no-op.</summary>
    [HttpPost("{deviceId}/cancel")]
    [ProducesResponseType(typeof(OnboardingCancelResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OnboardingCancelResponse>> Cancel(
        string deviceId,
        CancellationToken cancellationToken)
    {
        var state = await _workflow.CancelOnboardingAsync(deviceId, cancellationToken);
        return Ok(new OnboardingCancelResponse(state.Status));
    }
}
