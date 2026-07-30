using Greenhouse.Core.EdgeUnits;
using Greenhouse.Core.Onboarding;

namespace Greenhouse.Api.Tests;

/// <summary>Scriptable onboarding workflow: each operation returns whatever the test sets.</summary>
internal sealed class FakeOnboardingWorkflow : IOnboardingWorkflow
{
    public OnboardingState State { get; set; } = OnboardingState.Idle;

    public StartScanResult ScanResult { get; set; } = new StartScanResult.Started(OnboardingState.Idle);

    public SelectDeviceResult SelectResult { get; set; } = new SelectDeviceResult.Accepted(OnboardingState.Idle);

    public string? LastSelectedDeviceId { get; private set; }

    public string? LastCancelledDeviceId { get; private set; }

    public Task<OnboardingState> GetStateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(State);

    public Task<StartScanResult> StartOnboardingScanAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(ScanResult);

    public Task<SelectDeviceResult> SelectAndProvisionEdgeUnitAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        LastSelectedDeviceId = deviceId;
        return Task.FromResult(SelectResult);
    }

    public Task<OnboardingState> CancelOnboardingAsync(
        string deviceId,
        CancellationToken cancellationToken = default)
    {
        LastCancelledDeviceId = deviceId;
        return Task.FromResult(OnboardingState.Idle);
    }

    public Task CompleteOnboardingAsync(string deviceId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task CompleteMappingAsync(string deviceId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>In-memory Edge Unit store for controller tests.</summary>
internal sealed class FakeEdgeUnitRepository : IEdgeUnitRepository
{
    public Dictionary<string, EdgeUnit> Units { get; } = new(StringComparer.OrdinalIgnoreCase);

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

        var updated = unit with
        {
            UnitName = mapping.UnitName,
            Location = mapping.Location,
            MappingVersion = unit.MappingVersion + 1,
            MappingStatus = MappingStatuses.PublishPending,
        };

        Units[deviceId] = updated;
        return Task.FromResult<EdgeUnit?>(updated);
    }

    public Task UpdateMappingStatusAsync(
        string deviceId,
        string mappingStatus,
        bool clearTopologyDrift,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Records publish requests so tests can assert the endpoint queued one.</summary>
internal sealed class RecordingConfigurationPublisher : IEdgeUnitConfigurationPublisher
{
    public List<(string DeviceId, string Reason)> Requests { get; } = new();

    public void RequestPublish(string deviceId, string mappingReason) =>
        Requests.Add((deviceId, mappingReason));
}
