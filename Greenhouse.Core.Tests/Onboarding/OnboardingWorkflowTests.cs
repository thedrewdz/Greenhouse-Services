using Greenhouse.Core.Configuration;
using Greenhouse.Core.Onboarding;
using Greenhouse.Core.Tests.EdgeUnits;
using Greenhouse.Core.Tests.Setup;

namespace Greenhouse.Core.Tests.Onboarding;

/// <summary>
/// Covers the backend-owned onboarding session: scan, selection, auto-provisioning, cancellation,
/// and the transitions the UI observes. Timeouts are scaled to milliseconds so the no-device and
/// heartbeat-timeout paths run without real waiting.
/// </summary>
public class OnboardingWorkflowTests
{
    private const string DeviceId = "1ADD5912AF61";

    private static readonly OnboardingTimeouts FastTimeouts = new(
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(50));

    private static ProvisionableUnit Candidate(string deviceId = DeviceId, int? rssi = -60) =>
        new(deviceId, "AA:BB:CC:DD:EE:FF", "GH-Edge-" + deviceId, rssi);

    private sealed class Harness : IAsyncDisposable
    {
        public Harness(OnboardingTimeouts? timeouts = null)
        {
            Credentials.Current = new WifiCredentials("MyWifi", "secret");
            Network.LocalAddress = "192.168.1.50";

            Workflow = new OnboardingWorkflow(
                Transport,
                Credentials,
                Network,
                Sessions,
                Notifier,
                timeouts ?? FastTimeouts,
                TimeProvider.System);
        }

        public FakeProvisioningTransport Transport { get; } = new();

        public FakeWifiCredentialsRepository Credentials { get; } = new();

        public FakeNetworkConnector Network { get; } = new();

        public FakeOnboardingSessionRepository Sessions { get; } = new();

        public RecordingOnboardingNotifier Notifier { get; } = new();

        public OnboardingWorkflow Workflow { get; }

        public async Task WaitForStatusAsync(string status)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if ((await Workflow.GetStateAsync()).Status == status)
                {
                    return;
                }

                await Task.Delay(10);
            }

            var actual = (await Workflow.GetStateAsync()).Status;
            throw new TimeoutException($"Expected status '{status}' but the session stayed at '{actual}'.");
        }

        public ValueTask DisposeAsync() => Workflow.DisposeAsync();
    }

    [Fact]
    public async Task A_fresh_Main_Unit_reports_idle()
    {
        await using var harness = new Harness();

        var state = await harness.Workflow.GetStateAsync();

        Assert.Equal(OnboardingStatuses.Idle, state.Status);
        Assert.Empty(state.Candidates);
        Assert.Null(state.SelectedDeviceId);
        Assert.Null(state.ErrorCode);
    }

    [Fact]
    public async Task Starting_a_scan_reports_scanning_and_publishes_the_transition()
    {
        await using var harness = new Harness();
        // Keep the scan window open so the assertions observe the scanning state itself rather
        // than whatever the empty scan settles into.
        harness.Transport.HoldScanOpen = new TaskCompletionSource().Task;

        var result = await harness.Workflow.StartOnboardingScanAsync();

        var started = Assert.IsType<StartScanResult.Started>(result);
        Assert.Equal(OnboardingStatuses.Scanning, started.State.Status);
        Assert.Contains(OnboardingStatuses.Scanning, harness.Notifier.Statuses());
        Assert.Equal(OnboardingStatuses.Scanning, harness.Sessions.Current!.Status);
    }

    [Fact]
    public async Task Discovered_candidates_are_published_as_they_arrive()
    {
        await using var harness = new Harness();
        harness.Transport.Candidates.Add(Candidate());
        harness.Transport.Candidates.Add(Candidate("2BEEF0000001", -80));

        await harness.Workflow.StartOnboardingScanAsync();
        await harness.WaitForStatusAsync(OnboardingStatuses.CandidatesReady);

        Assert.Equal(2, harness.Notifier.Discovered.Count);
        var state = await harness.Workflow.GetStateAsync();
        // Strongest signal first, so the nearest unit is easiest to pick.
        Assert.Equal(new[] { DeviceId, "2BEEF0000001" }, state.Candidates.Select(c => c.DeviceId));
    }

    [Fact]
    public async Task A_repeat_observation_updates_a_candidate_without_re_announcing_it()
    {
        await using var harness = new Harness();
        harness.Transport.Candidates.Add(Candidate(rssi: null));
        harness.Transport.Candidates.Add(Candidate(rssi: -45));

        await harness.Workflow.StartOnboardingScanAsync();
        await harness.WaitForStatusAsync(OnboardingStatuses.CandidatesReady);

        Assert.Single(harness.Notifier.Discovered);
        var state = await harness.Workflow.GetStateAsync();
        Assert.Equal(-45, Assert.Single(state.Candidates).Rssi);
    }

    [Fact]
    public async Task A_scan_that_finds_nothing_ends_in_no_device_found()
    {
        await using var harness = new Harness();

        await harness.Workflow.StartOnboardingScanAsync();
        await harness.WaitForStatusAsync(OnboardingStatuses.NoDeviceFound);

        Assert.Contains(OnboardingStatuses.NoDeviceFound, harness.Notifier.Statuses());
    }

    [Fact]
    public async Task A_second_scan_is_refused_while_a_session_is_active()
    {
        await using var harness = new Harness();
        harness.Transport.Candidates.Add(Candidate());
        await harness.Workflow.StartOnboardingScanAsync();
        await harness.WaitForStatusAsync(OnboardingStatuses.CandidatesReady);

        var result = await harness.Workflow.StartOnboardingScanAsync();

        var active = Assert.IsType<StartScanResult.SessionActive>(result);
        Assert.Equal(OnboardingStatuses.CandidatesReady, active.State.Status);
    }

    [Fact]
    public async Task A_scan_may_be_restarted_from_a_terminal_state()
    {
        await using var harness = new Harness();
        await harness.Workflow.StartOnboardingScanAsync();
        await harness.WaitForStatusAsync(OnboardingStatuses.NoDeviceFound);

        var result = await harness.Workflow.StartOnboardingScanAsync();

        Assert.IsType<StartScanResult.Started>(result);
    }

    [Fact]
    public async Task Selecting_an_unknown_candidate_is_rejected()
    {
        await using var harness = new Harness();
        await harness.Workflow.StartOnboardingScanAsync();

        var result = await harness.Workflow.SelectAndProvisionEdgeUnitAsync("NOT-A-CANDIDATE");

        Assert.IsType<SelectDeviceResult.UnknownCandidate>(result);
    }

    [Fact]
    public async Task Selecting_a_candidate_provisions_it_with_derived_values_only()
    {
        await using var harness = new Harness();
        harness.Transport.Candidates.Add(Candidate());
        await harness.Workflow.StartOnboardingScanAsync();
        await harness.WaitForStatusAsync(OnboardingStatuses.CandidatesReady);

        var result = await harness.Workflow.SelectAndProvisionEdgeUnitAsync(DeviceId);
        await harness.WaitForStatusAsync(OnboardingStatuses.AwaitingHeartbeat);

        Assert.IsType<SelectDeviceResult.Accepted>(result);

        var payload = harness.Transport.LastPayload!;
        Assert.Equal(DeviceId, payload.DeviceId);
        Assert.Equal("MyWifi", payload.WifiSsid);
        Assert.Equal("secret", payload.WifiPassword);
        Assert.Equal("mqtt://192.168.1.50:1883", payload.MqttBrokerUri);
        // Omitted so the firmware default (30 000 ms) applies.
        Assert.Null(payload.HeartbeatIntervalMs);
    }

    [Fact]
    public async Task Repeating_start_for_the_selected_device_does_not_provision_twice()
    {
        await using var harness = new Harness();
        harness.Transport.Candidates.Add(Candidate());
        await harness.Workflow.StartOnboardingScanAsync();
        await harness.WaitForStatusAsync(OnboardingStatuses.CandidatesReady);
        await harness.Workflow.SelectAndProvisionEdgeUnitAsync(DeviceId);
        await harness.WaitForStatusAsync(OnboardingStatuses.AwaitingHeartbeat);

        var result = await harness.Workflow.SelectAndProvisionEdgeUnitAsync(DeviceId);

        var accepted = Assert.IsType<SelectDeviceResult.Accepted>(result);
        Assert.Equal(OnboardingStatuses.AwaitingHeartbeat, accepted.State.Status);
    }

    [Fact]
    public async Task Selecting_a_second_device_conflicts_with_the_active_selection()
    {
        await using var harness = new Harness();
        harness.Transport.Candidates.Add(Candidate());
        harness.Transport.Candidates.Add(Candidate("2BEEF0000001"));
        await harness.Workflow.StartOnboardingScanAsync();
        await harness.WaitForStatusAsync(OnboardingStatuses.CandidatesReady);
        await harness.Workflow.SelectAndProvisionEdgeUnitAsync(DeviceId);

        var result = await harness.Workflow.SelectAndProvisionEdgeUnitAsync("2BEEF0000001");

        var conflict = Assert.IsType<SelectDeviceResult.DifferentDeviceSelected>(result);
        Assert.Equal(DeviceId, conflict.State.SelectedDeviceId);
    }

    [Fact]
    public async Task A_rejected_provisioning_payload_fails_but_keeps_the_selected_device()
    {
        await using var harness = new Harness();
        harness.Transport.Candidates.Add(Candidate());
        harness.Transport.Result = new ProvisioningResult.Failed(2004, "mqtt_broker_uri_invalid");
        await harness.Workflow.StartOnboardingScanAsync();
        await harness.WaitForStatusAsync(OnboardingStatuses.CandidatesReady);

        await harness.Workflow.SelectAndProvisionEdgeUnitAsync(DeviceId);
        await harness.WaitForStatusAsync(OnboardingStatuses.Failed);

        var state = await harness.Workflow.GetStateAsync();
        Assert.Equal(2004, state.ErrorCode);
        Assert.Equal("mqtt_broker_uri_invalid", state.ErrorMessage);
        Assert.Equal(DeviceId, state.SelectedDeviceId);
    }

    [Fact]
    public async Task Missing_WiFi_credentials_fail_the_session_before_any_transport_work()
    {
        await using var harness = new Harness();
        harness.Credentials.Current = null;
        harness.Transport.Candidates.Add(Candidate());
        await harness.Workflow.StartOnboardingScanAsync();
        await harness.WaitForStatusAsync(OnboardingStatuses.CandidatesReady);

        await harness.Workflow.SelectAndProvisionEdgeUnitAsync(DeviceId);
        await harness.WaitForStatusAsync(OnboardingStatuses.Failed);

        Assert.Equal(2003, (await harness.Workflow.GetStateAsync()).ErrorCode);
        Assert.Null(harness.Transport.LastPayload);
    }

    [Fact]
    public async Task A_Main_Unit_with_no_local_address_cannot_derive_a_broker_uri()
    {
        await using var harness = new Harness();
        harness.Network.LocalAddress = null;
        harness.Transport.Candidates.Add(Candidate());
        await harness.Workflow.StartOnboardingScanAsync();
        await harness.WaitForStatusAsync(OnboardingStatuses.CandidatesReady);

        await harness.Workflow.SelectAndProvisionEdgeUnitAsync(DeviceId);
        await harness.WaitForStatusAsync(OnboardingStatuses.Failed);

        Assert.Equal(2004, (await harness.Workflow.GetStateAsync()).ErrorCode);
        Assert.Null(harness.Transport.LastPayload);
    }

    [Fact]
    public async Task The_first_heartbeat_moves_the_session_to_mapping_required()
    {
        await using var harness = new Harness(new OnboardingTimeouts(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromSeconds(30)));
        harness.Transport.Candidates.Add(Candidate());
        await harness.Workflow.StartOnboardingScanAsync();
        await harness.WaitForStatusAsync(OnboardingStatuses.CandidatesReady);
        await harness.Workflow.SelectAndProvisionEdgeUnitAsync(DeviceId);
        await harness.WaitForStatusAsync(OnboardingStatuses.AwaitingHeartbeat);

        await harness.Workflow.CompleteOnboardingAsync(DeviceId);

        Assert.Equal(OnboardingStatuses.MappingRequired, (await harness.Workflow.GetStateAsync()).Status);
        Assert.Contains(OnboardingStatuses.MappingRequired, harness.Notifier.Statuses());
    }

    [Fact]
    public async Task A_heartbeat_from_another_device_does_not_complete_the_session()
    {
        await using var harness = new Harness(new OnboardingTimeouts(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromSeconds(30)));
        harness.Transport.Candidates.Add(Candidate());
        await harness.Workflow.StartOnboardingScanAsync();
        await harness.WaitForStatusAsync(OnboardingStatuses.CandidatesReady);
        await harness.Workflow.SelectAndProvisionEdgeUnitAsync(DeviceId);
        await harness.WaitForStatusAsync(OnboardingStatuses.AwaitingHeartbeat);

        await harness.Workflow.CompleteOnboardingAsync("2BEEF0000001");

        Assert.Equal(OnboardingStatuses.AwaitingHeartbeat, (await harness.Workflow.GetStateAsync()).Status);
    }

    [Fact]
    public async Task No_heartbeat_within_the_timeout_fails_the_session()
    {
        await using var harness = new Harness();
        harness.Transport.Candidates.Add(Candidate());
        await harness.Workflow.StartOnboardingScanAsync();
        await harness.WaitForStatusAsync(OnboardingStatuses.CandidatesReady);

        await harness.Workflow.SelectAndProvisionEdgeUnitAsync(DeviceId);
        await harness.WaitForStatusAsync(OnboardingStatuses.Failed);

        var state = await harness.Workflow.GetStateAsync();
        Assert.NotNull(state.ErrorMessage);
        Assert.Contains(OnboardingStatuses.Failed, harness.Notifier.Statuses());
    }

    [Fact]
    public async Task Completing_the_mapping_completes_the_session()
    {
        await using var harness = new Harness(new OnboardingTimeouts(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromSeconds(30)));
        harness.Transport.Candidates.Add(Candidate());
        await harness.Workflow.StartOnboardingScanAsync();
        await harness.WaitForStatusAsync(OnboardingStatuses.CandidatesReady);
        await harness.Workflow.SelectAndProvisionEdgeUnitAsync(DeviceId);
        await harness.WaitForStatusAsync(OnboardingStatuses.AwaitingHeartbeat);
        await harness.Workflow.CompleteOnboardingAsync(DeviceId);

        await harness.Workflow.CompleteMappingAsync(DeviceId);

        Assert.Equal(OnboardingStatuses.Complete, (await harness.Workflow.GetStateAsync()).Status);
    }

    [Fact]
    public async Task Cancelling_returns_the_session_to_idle_and_clears_the_stored_row()
    {
        await using var harness = new Harness();
        harness.Transport.Candidates.Add(Candidate());
        await harness.Workflow.StartOnboardingScanAsync();
        await harness.WaitForStatusAsync(OnboardingStatuses.CandidatesReady);

        var state = await harness.Workflow.CancelOnboardingAsync(DeviceId);

        Assert.Equal(OnboardingStatuses.Idle, state.Status);
        Assert.Empty(state.Candidates);
        Assert.Null(harness.Sessions.Current);
    }

    [Fact]
    public async Task Cancelling_twice_is_a_no_op()
    {
        await using var harness = new Harness();
        await harness.Workflow.StartOnboardingScanAsync();
        await harness.Workflow.CancelOnboardingAsync(DeviceId);

        var state = await harness.Workflow.CancelOnboardingAsync(DeviceId);

        Assert.Equal(OnboardingStatuses.Idle, state.Status);
    }

    [Fact]
    public async Task Cancelling_a_different_device_leaves_the_active_session_alone()
    {
        await using var harness = new Harness(new OnboardingTimeouts(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromSeconds(30)));
        harness.Transport.Candidates.Add(Candidate());
        await harness.Workflow.StartOnboardingScanAsync();
        await harness.WaitForStatusAsync(OnboardingStatuses.CandidatesReady);
        await harness.Workflow.SelectAndProvisionEdgeUnitAsync(DeviceId);
        await harness.WaitForStatusAsync(OnboardingStatuses.AwaitingHeartbeat);

        var state = await harness.Workflow.CancelOnboardingAsync("2BEEF0000001");

        Assert.Equal(OnboardingStatuses.AwaitingHeartbeat, state.Status);
        Assert.Equal(DeviceId, state.SelectedDeviceId);
    }

    [Fact]
    public async Task A_notifier_failure_never_breaks_the_workflow()
    {
        var sessions = new FakeOnboardingSessionRepository();
        var credentials = new FakeWifiCredentialsRepository { Current = new WifiCredentials("MyWifi", "secret") };
        var transport = new FakeProvisioningTransport { HoldScanOpen = new TaskCompletionSource().Task };
        await using var workflow = new OnboardingWorkflow(
            transport,
            credentials,
            new FakeNetworkConnector { LocalAddress = "192.168.1.50" },
            sessions,
            new ThrowingNotifier(),
            FastTimeouts,
            TimeProvider.System);

        var result = await workflow.StartOnboardingScanAsync();

        Assert.IsType<StartScanResult.Started>(result);
        Assert.Equal(OnboardingStatuses.Scanning, sessions.Current!.Status);
    }

    [Fact]
    public async Task A_session_left_mid_flight_by_a_restart_is_not_reported_as_running()
    {
        await using var harness = new Harness();
        harness.Sessions.Current = new OnboardingSession(
            OnboardingStatuses.Scanning,
            DeviceId,
            DateTime.UtcNow,
            DateTime.UtcNow);

        var state = await harness.Workflow.GetStateAsync();

        Assert.Equal(OnboardingStatuses.Idle, state.Status);
        Assert.Null(harness.Sessions.Current);
    }

    [Fact]
    public async Task A_session_awaiting_mapping_survives_a_restart()
    {
        await using var harness = new Harness();
        harness.Sessions.Current = new OnboardingSession(
            OnboardingStatuses.MappingRequired,
            DeviceId,
            DateTime.UtcNow,
            DateTime.UtcNow);

        var state = await harness.Workflow.GetStateAsync();

        Assert.Equal(OnboardingStatuses.MappingRequired, state.Status);
        Assert.Equal(DeviceId, state.SelectedDeviceId);
    }

    private sealed class ThrowingNotifier : IOnboardingNotifier
    {
        public Task DeviceDiscoveredAsync(ProvisionableUnit candidate, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Hub is unavailable.");

        public Task StateChangedAsync(OnboardingStateChange change, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Hub is unavailable.");
    }
}
