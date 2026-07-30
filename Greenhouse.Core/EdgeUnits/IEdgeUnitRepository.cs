namespace Greenhouse.Core.EdgeUnits;

/// <summary>
/// Persistence contract for registered Edge Units and their slot topology. Implemented in the
/// storage project; application code depends only on this port.
/// </summary>
public interface IEdgeUnitRepository
{
    /// <summary>Returns the unit with <paramref name="deviceId"/>, or <c>null</c> when unknown.</summary>
    Task<EdgeUnit?> GetAsync(string deviceId, CancellationToken cancellationToken = default);

    /// <summary>Returns every registered unit, ordered by device id.</summary>
    Task<IReadOnlyList<EdgeUnit>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="heartbeat"/> to stored state and returns what it meant: registering
    /// a previously unknown unit, refreshing liveness, or raising the Drift Flag. Returns the
    /// reconciled unit so callers never need to read it back.
    /// </summary>
    /// <remarks>
    /// The read, the decision (<see cref="HeartbeatReconciliation.Reconcile"/>), and the write are
    /// one atomic operation by contract. Heartbeat ingestion runs concurrently with the mapping
    /// endpoint, so deciding against a separately-read snapshot would let a heartbeat revert a
    /// mapping accepted in between — there is deliberately no whole-unit write on this port.
    /// </remarks>
    Task<HeartbeatOutcome> RecordHeartbeatAsync(
        HeartbeatMessage heartbeat,
        DateTime receivedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="mapping"/> atomically: increments <c>MappingVersion</c> by one,
    /// sets <c>MappingStatus</c> to <see cref="MappingStatuses.PublishPending"/>, and writes the
    /// assigned role, capability, and label onto the matching stored slots. Returns the updated
    /// unit, or <c>null</c> when <paramref name="deviceId"/> is unknown.
    /// </summary>
    Task<EdgeUnit?> UpdateMappingAsync(
        string deviceId,
        EdgeUnitMapping mapping,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the outcome of a configuration publish. When
    /// <paramref name="clearTopologyDrift"/> is <c>true</c> the Drift Flag is cleared — only an
    /// acknowledgement with <c>result=success</c> may do that.
    /// </summary>
    Task UpdateMappingStatusAsync(
        string deviceId,
        string mappingStatus,
        bool clearTopologyDrift,
        CancellationToken cancellationToken = default);
}
