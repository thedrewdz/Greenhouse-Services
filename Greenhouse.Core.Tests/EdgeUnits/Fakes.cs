using System.Runtime.CompilerServices;
using Greenhouse.Core.EdgeUnits;
using Greenhouse.Core.Messaging;
using Greenhouse.Core.Onboarding;

namespace Greenhouse.Core.Tests.EdgeUnits;

/// <summary>In-memory Edge Unit store that records the mapping-status transitions applied to it.</summary>
internal sealed class FakeEdgeUnitRepository : IEdgeUnitRepository
{
    private readonly List<(string DeviceId, string Status, bool ClearedDrift)> _statusUpdates = new();
    private readonly object _sync = new();

    public Dictionary<string, EdgeUnit> Units { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A snapshot: the publisher writes these from its background pump while a test reads them.
    /// </summary>
    public IReadOnlyList<(string DeviceId, string Status, bool ClearedDrift)> StatusUpdates
    {
        get
        {
            lock (_sync)
            {
                return _statusUpdates.ToArray();
            }
        }
    }

    public Task<EdgeUnit?> GetAsync(string deviceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Units.TryGetValue(deviceId, out var unit) ? unit : null);

    public Task<IReadOnlyList<EdgeUnit>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EdgeUnit>>(Units.Values.OrderBy(u => u.DeviceId).ToArray());

    public Task UpsertAsync(EdgeUnit edgeUnit, CancellationToken cancellationToken = default)
    {
        Units[edgeUnit.DeviceId] = edgeUnit;
        return Task.CompletedTask;
    }

    public Task<EdgeUnit?> UpdateMappingAsync(
        string deviceId,
        EdgeUnitMapping mapping,
        CancellationToken cancellationToken = default)
    {
        if (!Units.TryGetValue(deviceId, out var unit))
        {
            return Task.FromResult<EdgeUnit?>(null);
        }

        var slots = unit.Slots
            .Select(slot =>
            {
                var assignment = mapping.Slots.FirstOrDefault(s => s.SlotId == slot.SlotId);
                return assignment is null
                    ? slot
                    : slot with
                    {
                        Role = assignment.Role,
                        Capability = assignment.Capability,
                        Label = assignment.Label,
                    };
            })
            .ToArray();

        var updated = unit with
        {
            UnitName = mapping.UnitName,
            Location = mapping.Location,
            MappingVersion = unit.MappingVersion + 1,
            MappingStatus = MappingStatuses.PublishPending,
            Slots = slots,
        };

        Units[deviceId] = updated;
        return Task.FromResult<EdgeUnit?>(updated);
    }

    public Task UpdateMappingStatusAsync(
        string deviceId,
        string mappingStatus,
        bool clearTopologyDrift,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _statusUpdates.Add((deviceId, mappingStatus, clearTopologyDrift));
        }

        if (Units.TryGetValue(deviceId, out var unit))
        {
            Units[deviceId] = unit with
            {
                MappingStatus = mappingStatus,
                TopologyDriftDetectedAt = clearTopologyDrift ? null : unit.TopologyDriftDetectedAt,
            };
        }

        return Task.CompletedTask;
    }
}

/// <summary>Records publish requests without doing any transport work.</summary>
internal sealed class RecordingConfigurationPublisher : IEdgeUnitConfigurationPublisher
{
    public List<(string DeviceId, string Reason)> Requests { get; } = new();

    public void RequestPublish(string deviceId, string mappingReason) =>
        Requests.Add((deviceId, mappingReason));
}

/// <summary>
/// In-memory messaging service. Captures published messages and lets a test deliver an inbound
/// message to the handlers registered for a matching topic.
/// </summary>
internal sealed class FakeMessagingService : IMessagingService
{
    private readonly Dictionary<string, Func<MessageEnvelope, Task>> _handlers = new();
    private readonly List<(string Topic, string Payload)> _published = new();
    private readonly object _sync = new();

    /// <summary>A snapshot: publishes happen on the publisher's pump while a test reads them.</summary>
    public IReadOnlyList<(string Topic, string Payload)> Published
    {
        get
        {
            lock (_sync)
            {
                return _published.ToArray();
            }
        }
    }

    /// <summary>Raised after each publish so a test can respond as the Edge Unit would.</summary>
    public Func<string, string, Task>? OnPublish { get; set; }

    public async Task PublishAsync(string topic, string payload, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _published.Add((topic, payload));
        }

        if (OnPublish is not null)
        {
            await OnPublish(topic, payload);
        }
    }

    public void Subscribe(string topicPattern, Func<MessageEnvelope, Task> handler) =>
        _handlers[topicPattern] = handler;

    public void Unsubscribe(string topicPattern) => _handlers.Remove(topicPattern);

    public Task DeliverAsync(string topic, string payload)
    {
        var envelope = new MessageEnvelope(topic, payload, DateTime.UtcNow);
        return Task.WhenAll(_handlers.Values.Select(handler => handler(envelope)));
    }
}

/// <summary>Records the onboarding calls the heartbeat and publish paths make.</summary>
internal sealed class RecordingOnboardingWorkflow : IOnboardingWorkflow
{
    private readonly List<string> _completed = new();
    private readonly List<string> _mappingCompleted = new();
    private readonly object _sync = new();

    public IReadOnlyList<string> Completed
    {
        get
        {
            lock (_sync)
            {
                return _completed.ToArray();
            }
        }
    }

    public IReadOnlyList<string> MappingCompleted
    {
        get
        {
            lock (_sync)
            {
                return _mappingCompleted.ToArray();
            }
        }
    }

    public Task<OnboardingState> GetStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OnboardingState.Idle);

    public Task<StartScanResult> StartOnboardingScanAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<StartScanResult>(new StartScanResult.Started(OnboardingState.Idle));

    public Task<SelectDeviceResult> SelectAndProvisionEdgeUnitAsync(
        string deviceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<SelectDeviceResult>(new SelectDeviceResult.Accepted(OnboardingState.Idle));

    public Task<OnboardingState> CancelOnboardingAsync(
        string deviceId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(OnboardingState.Idle);

    public Task CompleteOnboardingAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _completed.Add(deviceId);
        }

        return Task.CompletedTask;
    }

    public Task CompleteMappingAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            _mappingCompleted.Add(deviceId);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Scriptable provisioning transport: yields candidates on demand and returns a set result.</summary>
internal sealed class FakeProvisioningTransport : IEdgeUnitProvisioningTransport
{
    public List<ProvisionableUnit> Candidates { get; } = new();

    public ProvisioningResult Result { get; set; } = new ProvisioningResult.Success();

    public ProvisioningPayload? LastPayload { get; private set; }

    public ProvisionableUnit? LastUnit { get; private set; }

    /// <summary>Completes once the scan enumeration has yielded everything queued.</summary>
    public TaskCompletionSource ScanDrained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>When set, the scan blocks on this after yielding, simulating a still-open window.</summary>
    public Task? HoldScanOpen { get; set; }

    public async IAsyncEnumerable<ProvisionableUnit> ScanForProvisionableUnitsAsync(
        TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var candidate in Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return candidate;
        }

        ScanDrained.TrySetResult();

        if (HoldScanOpen is not null)
        {
            await HoldScanOpen.WaitAsync(cancellationToken);
        }
    }

    public Task<ProvisioningResult> ProvisionUnitAsync(
        ProvisionableUnit unit,
        ProvisioningPayload payload,
        CancellationToken cancellationToken = default)
    {
        LastUnit = unit;
        LastPayload = payload;
        return Task.FromResult(Result);
    }
}

/// <summary>In-memory single-row onboarding session store.</summary>
internal sealed class FakeOnboardingSessionRepository : IOnboardingSessionRepository
{
    public OnboardingSession? Current { get; set; }

    public int ClearCount { get; private set; }

    public Task<OnboardingSession?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(Current);

    public Task SaveAsync(OnboardingSession session, CancellationToken cancellationToken = default)
    {
        Current = session;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        ClearCount++;
        Current = null;
        return Task.CompletedTask;
    }
}

/// <summary>Records every hub event the workflow raises, in order.</summary>
internal sealed class RecordingOnboardingNotifier : IOnboardingNotifier
{
    private readonly object _sync = new();

    public List<ProvisionableUnit> Discovered { get; } = new();

    public List<OnboardingStateChange> Changes { get; } = new();

    public Task DeviceDiscoveredAsync(ProvisionableUnit candidate, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            Discovered.Add(candidate);
        }

        return Task.CompletedTask;
    }

    public Task StateChangedAsync(OnboardingStateChange change, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            Changes.Add(change);
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<string> Statuses()
    {
        lock (_sync)
        {
            return Changes.Select(c => c.Status).ToArray();
        }
    }
}
