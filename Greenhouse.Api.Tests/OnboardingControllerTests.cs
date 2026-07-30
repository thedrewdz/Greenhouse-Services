using Greenhouse.Api.Contracts;
using Greenhouse.Api.Controllers;
using Greenhouse.Core.Onboarding;
using Microsoft.AspNetCore.Mvc;

namespace Greenhouse.Api.Tests;

/// <summary>
/// Contract tests for the onboarding resources: the status codes and body shapes the UI client is
/// generated from.
/// </summary>
public class OnboardingControllerTests
{
    private const string DeviceId = "1ADD5912AF61";

    private static ProvisionableUnit Candidate() =>
        new(DeviceId, "AA:BB:CC:DD:EE:FF", "GH-Edge-" + DeviceId, -60);

    private static OnboardingState Scanning() => new(
        OnboardingStatuses.Scanning,
        new[] { Candidate() },
        SelectedDeviceId: null,
        ErrorCode: null,
        ErrorMessage: null);

    [Fact]
    public async Task Scan_accepts_and_reports_scanning()
    {
        var workflow = new FakeOnboardingWorkflow
        {
            ScanResult = new StartScanResult.Started(Scanning()),
        };

        var result = await new OnboardingController(workflow).Scan(CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Equal(OnboardingStatuses.Scanning, Assert.IsType<OnboardingScanResponse>(accepted.Value).Status);
    }

    [Fact]
    public async Task Scan_conflicts_when_a_session_is_already_active()
    {
        var workflow = new FakeOnboardingWorkflow
        {
            ScanResult = new StartScanResult.SessionActive(Scanning()),
        };

        var result = await new OnboardingController(workflow).Scan(CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var body = Assert.IsType<OnboardingStateResponse>(conflict.Value);
        Assert.Equal(OnboardingStatuses.Scanning, body.Status);
    }

    [Fact]
    public async Task Get_returns_the_full_state_including_candidates()
    {
        var workflow = new FakeOnboardingWorkflow { State = Scanning() };

        var result = await new OnboardingController(workflow).Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<OnboardingStateResponse>(ok.Value);
        Assert.Equal(OnboardingStatuses.Scanning, body.Status);
        var candidate = Assert.Single(body.Candidates);
        // The candidate identifier is the Edge Unit hardware id, never the transport address.
        Assert.Equal(DeviceId, candidate.DeviceId);
        Assert.Equal("GH-Edge-" + DeviceId, candidate.AdvertisedName);
        Assert.Equal(-60, candidate.Rssi);
        Assert.Null(body.SelectedDeviceId);
        Assert.Null(body.ErrorCode);
        Assert.Null(body.ErrorMessage);
    }

    [Fact]
    public async Task Get_on_an_idle_Main_Unit_returns_an_empty_candidate_list()
    {
        var result = await new OnboardingController(new FakeOnboardingWorkflow()).Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<OnboardingStateResponse>(ok.Value);
        Assert.Equal(OnboardingStatuses.Idle, body.Status);
        Assert.Empty(body.Candidates);
    }

    [Fact]
    public async Task Start_accepts_and_echoes_the_selected_device()
    {
        var provisioning = new OnboardingState(
            OnboardingStatuses.Provisioning,
            Array.Empty<ProvisionableUnit>(),
            DeviceId,
            null,
            null);
        var workflow = new FakeOnboardingWorkflow
        {
            SelectResult = new SelectDeviceResult.Accepted(provisioning),
        };

        var result = await new OnboardingController(workflow).Start(DeviceId, CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        var body = Assert.IsType<OnboardingStartResponse>(accepted.Value);
        Assert.Equal(OnboardingStatuses.Provisioning, body.Status);
        Assert.Equal(DeviceId, body.DeviceId);
        Assert.Equal(DeviceId, workflow.LastSelectedDeviceId);
    }

    [Fact]
    public async Task Start_returns_404_when_the_device_is_not_a_current_candidate()
    {
        var workflow = new FakeOnboardingWorkflow
        {
            SelectResult = new SelectDeviceResult.UnknownCandidate(),
        };

        var result = await new OnboardingController(workflow).Start("NOPE", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Start_returns_409_when_a_different_device_is_already_selected()
    {
        var active = new OnboardingState(
            OnboardingStatuses.Provisioning,
            Array.Empty<ProvisionableUnit>(),
            DeviceId,
            null,
            null);
        var workflow = new FakeOnboardingWorkflow
        {
            SelectResult = new SelectDeviceResult.DifferentDeviceSelected(active),
        };

        var result = await new OnboardingController(workflow).Start("2BEEF0000001", CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(DeviceId, Assert.IsType<OnboardingStateResponse>(conflict.Value).SelectedDeviceId);
    }

    [Fact]
    public async Task Cancel_returns_idle()
    {
        var workflow = new FakeOnboardingWorkflow();

        var result = await new OnboardingController(workflow).Cancel(DeviceId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(OnboardingStatuses.Idle, Assert.IsType<OnboardingCancelResponse>(ok.Value).Status);
        Assert.Equal(DeviceId, workflow.LastCancelledDeviceId);
    }

    [Fact]
    public async Task A_failed_state_exposes_the_error_code_and_message()
    {
        var workflow = new FakeOnboardingWorkflow
        {
            State = new OnboardingState(
                OnboardingStatuses.Failed,
                Array.Empty<ProvisionableUnit>(),
                DeviceId,
                2004,
                "mqtt_broker_uri_invalid"),
        };

        var result = await new OnboardingController(workflow).Get(CancellationToken.None);

        var body = Assert.IsType<OnboardingStateResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(2004, body.ErrorCode);
        Assert.Equal("mqtt_broker_uri_invalid", body.ErrorMessage);
        // Selected device context survives a failure so the operator can retry.
        Assert.Equal(DeviceId, body.SelectedDeviceId);
    }
}
