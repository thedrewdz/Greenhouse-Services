using Greenhouse.Core.Configuration;
using Greenhouse.Core.Networking;

namespace Greenhouse.Core.Onboarding;

/// <summary>
/// The single, process-wide owner of the Edge Unit onboarding session. Scanning and
/// provisioning run as background work started by a request but outliving it, so the session's
/// status, candidates, and cancellation live here rather than in any request scope.
/// </summary>
/// <remarks>
/// <para>
/// All state transitions are serialised through one mutex. Notifications are raised after the
/// mutex is released so a slow observer can never stall the workflow, and notification failures
/// are swallowed — the hub is an observation channel and <c>GET /api/onboarding</c> stays
/// authoritative.
/// </para>
/// <para>
/// Discovered candidates are held in memory only: they are meaningful only while the units are
/// still advertising. The status row is persisted so a UI reconnecting after a daemon restart
/// still sees real backend state.
/// </para>
/// </remarks>
public sealed class OnboardingWorkflow : IOnboardingWorkflow, IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Broker port for the derived bootstrap URI. Edge Units connect to the Mosquitto instance
    /// on the Main Unit, which listens on the MQTT default.
    /// </summary>
    private const int BrokerPort = 1883;

    // Canonical onboarding error codes reused for the Main Unit preconditions that make a
    // provisioning payload impossible to build (specs/edge-unit-onboarding/spec.md).
    private const int WifiSsidEmpty = 2003;
    private const int MqttBrokerUriInvalid = 2004;

    private readonly IEdgeUnitProvisioningTransport _transport;
    private readonly IWifiCredentialsRepository _credentials;
    private readonly INetworkConnector _network;
    private readonly IOnboardingSessionRepository _sessions;
    private readonly IOnboardingNotifier _notifier;
    private readonly OnboardingTimeouts _timeouts;
    private readonly TimeProvider _timeProvider;

    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly Dictionary<string, ProvisionableUnit> _candidates = new(StringComparer.OrdinalIgnoreCase);

    private bool _loaded;
    private string _status = OnboardingStatuses.Idle;
    private string? _selectedDeviceId;
    private int? _errorCode;
    private string? _errorMessage;
    private DateTime _startedAt;

    private CancellationTokenSource? _sessionCts;
    private CancellationTokenSource? _heartbeatCts;
    private Task _background = Task.CompletedTask;

    public OnboardingWorkflow(
        IEdgeUnitProvisioningTransport transport,
        IWifiCredentialsRepository credentials,
        INetworkConnector network,
        IOnboardingSessionRepository sessions,
        IOnboardingNotifier notifier,
        OnboardingTimeouts timeouts,
        TimeProvider timeProvider)
    {
        _transport = transport;
        _credentials = credentials;
        _network = network;
        _sessions = sessions;
        _notifier = notifier;
        _timeouts = timeouts;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Statuses that mean a session is in progress. A new scan is refused while one of these is
    /// current; the terminal statuses (idle, complete, failed, no-device-found) allow a restart.
    /// </summary>
    private static bool IsSessionActive(string status) => status is
        OnboardingStatuses.Scanning or
        OnboardingStatuses.CandidatesReady or
        OnboardingStatuses.Provisioning or
        OnboardingStatuses.AwaitingHeartbeat or
        OnboardingStatuses.MappingRequired;

    /// <summary>
    /// Statuses that cannot survive a daemon restart: they describe work that was in flight in a
    /// process that no longer exists.
    /// </summary>
    private static bool IsTransientStatus(string status) => status is
        OnboardingStatuses.Scanning or
        OnboardingStatuses.CandidatesReady or
        OnboardingStatuses.Provisioning or
        OnboardingStatuses.AwaitingHeartbeat;

    public async Task<OnboardingState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return Snapshot();
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<StartScanResult> StartOnboardingScanAsync(CancellationToken cancellationToken = default)
    {
        OnboardingState state;

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);

            if (IsSessionActive(_status))
            {
                return new StartScanResult.SessionActive(Snapshot());
            }

            _candidates.Clear();
            _selectedDeviceId = null;
            _errorCode = null;
            _errorMessage = null;
            _status = OnboardingStatuses.Scanning;
            _startedAt = _timeProvider.GetUtcNow().UtcDateTime;

            _sessionCts?.Dispose();
            _sessionCts = new CancellationTokenSource();
            var sessionToken = _sessionCts.Token;

            await PersistAsync(cancellationToken);
            state = Snapshot();

            // Scanning must begin promptly and continue independently of the request that asked
            // for it: navigating away in the UI must not stop the backend scan.
            Track(Task.Run(() => RunScanAsync(sessionToken), CancellationToken.None));
        }
        finally
        {
            _mutex.Release();
        }

        await NotifyStateAsync(state);
        return new StartScanResult.Started(state);
    }

    public async Task<SelectDeviceResult> SelectAndProvisionEdgeUnitAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        OnboardingState state;
        ProvisionableUnit candidate;

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);

            // Idempotent: the same start request for the device already being provisioned
            // returns current state instead of repeating BLE work.
            if (string.Equals(_selectedDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                return new SelectDeviceResult.Accepted(Snapshot());
            }

            if (_selectedDeviceId is not null)
            {
                return new SelectDeviceResult.DifferentDeviceSelected(Snapshot());
            }

            if (!_candidates.TryGetValue(deviceId, out var found))
            {
                return new SelectDeviceResult.UnknownCandidate();
            }

            candidate = found;
            _selectedDeviceId = candidate.DeviceId;
            _errorCode = null;
            _errorMessage = null;
            _status = OnboardingStatuses.Provisioning;

            // Stop scanning: the selected-device handoff ends the scan window.
            var previousSession = _sessionCts;
            _sessionCts = new CancellationTokenSource();
            var sessionToken = _sessionCts.Token;
            previousSession?.Cancel();
            previousSession?.Dispose();

            await PersistAsync(cancellationToken);
            state = Snapshot();

            Track(Task.Run(() => RunProvisioningAsync(candidate, sessionToken), CancellationToken.None));
        }
        finally
        {
            _mutex.Release();
        }

        await NotifyStateAsync(state);
        return new SelectDeviceResult.Accepted(state);
    }

    public async Task<OnboardingState> CancelOnboardingAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        OnboardingState state;

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);

            // Never let a cancel for one device tear down another device's session.
            if (_selectedDeviceId is not null
                && !string.Equals(_selectedDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                return Snapshot();
            }

            await ResetToIdleAsync(cancellationToken);
            state = Snapshot();
        }
        finally
        {
            _mutex.Release();
        }

        await NotifyStateAsync(state);
        return state;
    }

    public async Task CompleteOnboardingAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        OnboardingState state;

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);

            if (_status != OnboardingStatuses.AwaitingHeartbeat
                || !string.Equals(_selectedDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _status = OnboardingStatuses.MappingRequired;
            await PersistAsync(cancellationToken);
            state = Snapshot();

            // End the 90-second wait immediately rather than letting it expire into a failure.
            _heartbeatCts?.Cancel();
        }
        finally
        {
            _mutex.Release();
        }

        await NotifyStateAsync(state);
    }

    public async Task CompleteMappingAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        OnboardingState state;

        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);

            if (_status != OnboardingStatuses.MappingRequired
                || !string.Equals(_selectedDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _status = OnboardingStatuses.Complete;
            await PersistAsync(cancellationToken);
            state = Snapshot();
        }
        finally
        {
            _mutex.Release();
        }

        await NotifyStateAsync(state);
    }

    private async Task RunScanAsync(CancellationToken sessionToken)
    {
        try
        {
            await foreach (var unit in _transport
                               .ScanForProvisionableUnitsAsync(_timeouts.Scan, sessionToken)
                               .WithCancellation(sessionToken))
            {
                var isNewCandidate = false;

                await _mutex.WaitAsync(sessionToken);
                try
                {
                    if (_status != OnboardingStatuses.Scanning)
                    {
                        return;
                    }

                    // A repeat observation refines an existing candidate (RSSI usually arrives
                    // after the name); only the first sighting is a discovery.
                    isNewCandidate = !_candidates.ContainsKey(unit.DeviceId);
                    _candidates[unit.DeviceId] = unit;
                }
                finally
                {
                    _mutex.Release();
                }

                if (isNewCandidate)
                {
                    await NotifyDiscoveredAsync(unit);
                }
            }

            await EndScanWindowAsync(sessionToken);
        }
        catch (OperationCanceledException)
        {
            // Cancelled by device selection or an explicit cancel; both own the next transition.
        }
        catch (Exception ex)
        {
            await FailAsync(OnboardingStatuses.Scanning, errorCode: null, $"BLE scan failed: {ex.Message}");
        }
    }

    private async Task EndScanWindowAsync(CancellationToken sessionToken)
    {
        OnboardingState state;

        await _mutex.WaitAsync(sessionToken);
        try
        {
            if (_status != OnboardingStatuses.Scanning)
            {
                return;
            }

            _status = _candidates.Count > 0
                ? OnboardingStatuses.CandidatesReady
                : OnboardingStatuses.NoDeviceFound;

            await PersistAsync(sessionToken);
            state = Snapshot();
        }
        finally
        {
            _mutex.Release();
        }

        await NotifyStateAsync(state);
    }

    private async Task RunProvisioningAsync(ProvisionableUnit candidate, CancellationToken sessionToken)
    {
        try
        {
            var payload = await BuildPayloadAsync(candidate, sessionToken);
            if (payload is null)
            {
                return;
            }

            var result = await _transport.ProvisionUnitAsync(candidate, payload, sessionToken);
            if (result is ProvisioningResult.Failed failed)
            {
                // Keep the selected device so the operator can retry without reselecting.
                await FailAsync(OnboardingStatuses.Provisioning, failed.ErrorCode, failed.ErrorMessage);
                return;
            }

            await AwaitFirstHeartbeatAsync(sessionToken);
        }
        catch (OperationCanceledException)
        {
            // Cancelled explicitly; CancelOnboardingAsync owns the transition to idle.
        }
        catch (Exception ex)
        {
            await FailAsync(
                OnboardingStatuses.Provisioning,
                errorCode: null,
                $"Provisioning failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds the provisioning payload entirely from Main Unit state — the operator supplies no
    /// credentials during onboarding. Returns <c>null</c> after failing the session when a
    /// required value is unavailable.
    /// </summary>
    private async Task<ProvisioningPayload?> BuildPayloadAsync(
        ProvisionableUnit candidate,
        CancellationToken cancellationToken)
    {
        var credentials = await _credentials.GetAsync();
        if (credentials is null || string.IsNullOrWhiteSpace(credentials.NetworkName))
        {
            await FailAsync(
                OnboardingStatuses.Provisioning,
                WifiSsidEmpty,
                "No WiFi credentials are stored on the Main Unit; complete Main Unit setup first.");
            return null;
        }

        var localAddress = await _network.GetLocalAddressAsync(cancellationToken);
        if (localAddress is null)
        {
            await FailAsync(
                OnboardingStatuses.Provisioning,
                MqttBrokerUriInvalid,
                "The Main Unit has no local network address, so the MQTT broker URI cannot be derived.");
            return null;
        }

        // heartbeat_interval_ms is omitted deliberately: the firmware default (30 000 ms) applies.
        return new ProvisioningPayload(
            candidate.DeviceId,
            credentials.NetworkName,
            credentials.Password,
            $"mqtt://{localAddress}:{BrokerPort}");
    }

    private async Task AwaitFirstHeartbeatAsync(CancellationToken sessionToken)
    {
        OnboardingState accepted;
        CancellationToken heartbeatToken;

        await _mutex.WaitAsync(sessionToken);
        try
        {
            if (_status != OnboardingStatuses.Provisioning)
            {
                return;
            }

            _status = OnboardingStatuses.AwaitingHeartbeat;
            _heartbeatCts?.Dispose();
            _heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
            heartbeatToken = _heartbeatCts.Token;

            await PersistAsync(sessionToken);
            accepted = Snapshot();
        }
        finally
        {
            _mutex.Release();
        }

        await NotifyStateAsync(accepted);

        try
        {
            await Task.Delay(_timeouts.Heartbeat, _timeProvider, heartbeatToken);
        }
        catch (OperationCanceledException)
        {
            // The heartbeat arrived (or the session was cancelled); either way the wait is over
            // and whoever cancelled it owns the resulting state.
            return;
        }

        // Main Unit never auto-retries after this timeout: the operator restarts from the UI.
        await FailAsync(
            OnboardingStatuses.AwaitingHeartbeat,
            errorCode: null,
            $"No heartbeat was received within {_timeouts.Heartbeat.TotalSeconds:0} seconds of provisioning.");
    }

    /// <summary>
    /// Fails the session, but only while it is still in <paramref name="expectedStatus"/> — the
    /// state the failing work owns. A cancel that already moved the session to idle must not be
    /// overwritten by a transport exception that arrives afterwards, which is otherwise exactly
    /// what happens when the transport throws something other than a cancellation.
    /// </summary>
    private async Task FailAsync(string expectedStatus, int? errorCode, string errorMessage)
    {
        OnboardingState state;

        await _mutex.WaitAsync(CancellationToken.None);
        try
        {
            if (_status != expectedStatus)
            {
                return;
            }

            _status = OnboardingStatuses.Failed;
            _errorCode = errorCode;
            _errorMessage = errorMessage;
            await PersistAsync(CancellationToken.None);
            state = Snapshot();
        }
        finally
        {
            _mutex.Release();
        }

        await NotifyStateAsync(state);
    }

    /// <summary>Loads persisted session state once per process. Callers must hold the mutex.</summary>
    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        // Set only after a successful read: a failed load must be retried, not silently latched
        // into reporting idle for the rest of the process lifetime.
        var session = await _sessions.GetCurrentAsync(cancellationToken);
        _loaded = true;

        if (session is null)
        {
            return;
        }

        if (IsTransientStatus(session.Status))
        {
            // The scan or provisioning task died with the previous process; there is nothing to
            // resume and reporting it as still running would mislead the UI.
            await _sessions.ClearAsync(cancellationToken);
            return;
        }

        _status = session.Status;
        _selectedDeviceId = session.SelectedDeviceId;
        _startedAt = session.StartedAt;
    }

    /// <summary>Callers must hold the mutex.</summary>
    private async Task ResetToIdleAsync(CancellationToken cancellationToken)
    {
        _sessionCts?.Cancel();
        _sessionCts?.Dispose();
        _sessionCts = null;
        _heartbeatCts?.Cancel();
        _heartbeatCts?.Dispose();
        _heartbeatCts = null;

        _candidates.Clear();
        _status = OnboardingStatuses.Idle;
        _selectedDeviceId = null;
        _errorCode = null;
        _errorMessage = null;

        await _sessions.ClearAsync(cancellationToken);
    }

    /// <summary>
    /// Adds <paramref name="task"/> to the background work dispose waits on, keeping any
    /// predecessor that has not finished unwinding yet. Callers must hold the mutex.
    /// </summary>
    private void Track(Task task) =>
        _background = _background.IsCompleted ? task : Task.WhenAll(_background, task);

    /// <summary>Callers must hold the mutex.</summary>
    private Task PersistAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (_startedAt == default)
        {
            _startedAt = now;
        }

        return _sessions.SaveAsync(
            new OnboardingSession(_status, _selectedDeviceId, _startedAt, now),
            cancellationToken);
    }

    /// <summary>Callers must hold the mutex.</summary>
    private OnboardingState Snapshot() => new(
        _status,
        _candidates.Values.OrderByDescending(c => c.Rssi ?? int.MinValue).ToArray(),
        _selectedDeviceId,
        _errorCode,
        _errorMessage);

    private async Task NotifyStateAsync(OnboardingState state)
    {
        try
        {
            await _notifier.StateChangedAsync(OnboardingStateChange.From(state), CancellationToken.None);
        }
        catch
        {
            // Observation channel only — a disconnected or faulting observer must never affect
            // the workflow, and backend state remains readable over REST.
        }
    }

    private async Task NotifyDiscoveredAsync(ProvisionableUnit candidate)
    {
        try
        {
            await _notifier.DeviceDiscoveredAsync(candidate, CancellationToken.None);
        }
        catch
        {
            // See NotifyStateAsync.
        }
    }

    /// <summary>Cancels in-flight work and waits for it to unwind. The preferred shutdown path.</summary>
    public async ValueTask DisposeAsync()
    {
        _sessionCts?.Cancel();

        try
        {
            await _background;
        }
        catch
        {
            // Background work is best-effort during shutdown.
        }

        _sessionCts?.Dispose();
        _heartbeatCts?.Dispose();
        _mutex.Dispose();
    }

    /// <summary>
    /// Synchronous fallback for containers disposed without <c>DisposeAsync</c>. It cancels the
    /// background work but cannot wait for it, so the mutex is left for the finalizer rather than
    /// disposed out from under a task still waiting on it. Implementing only
    /// <see cref="IAsyncDisposable"/> would make such a container throw on dispose.
    /// </summary>
    public void Dispose() => _sessionCts?.Cancel();
}
