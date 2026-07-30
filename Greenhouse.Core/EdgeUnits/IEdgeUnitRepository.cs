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
    /// Inserts or replaces <paramref name="edgeUnit"/> together with its slot topology. Used by
    /// heartbeat processing to register a new unit and to refresh observed topology; the write
    /// is atomic, so a partially applied topology is never observable.
    /// </summary>
    Task UpsertAsync(EdgeUnit edgeUnit, CancellationToken cancellationToken = default);

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
