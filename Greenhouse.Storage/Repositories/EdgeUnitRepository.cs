using Greenhouse.Core.EdgeUnits;
using Greenhouse.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace Greenhouse.Storage.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IEdgeUnitRepository"/> over the <c>EdgeUnits</c> and
/// <c>SlotTopologies</c> tables. Maps between entities and application models at this boundary
/// so no entity type escapes <c>Greenhouse.Storage</c>.
/// </summary>
/// <remarks>
/// Every write is a single <c>SaveChangesAsync</c> so a mapping or topology update is applied
/// whole or not at all — runtime consumers never observe a partially written mapping.
/// </remarks>
public sealed class EdgeUnitRepository : IEdgeUnitRepository
{
    private readonly GreenhouseDatabase _database;

    public EdgeUnitRepository(GreenhouseDatabase database)
    {
        _database = database;
    }

    public Task<EdgeUnit?> GetAsync(string deviceId, CancellationToken cancellationToken = default) =>
        _database.ExecuteAsync(
            async (context, ct) =>
            {
                var entity = await QueryWithSlots(context)
                    .FirstOrDefaultAsync(e => e.DeviceId == deviceId, ct);
                return entity is null ? null : MapToModel(entity);
            },
            cancellationToken);

    public Task<IReadOnlyList<EdgeUnit>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _database.ExecuteAsync(
            async (context, ct) =>
            {
                var entities = await QueryWithSlots(context)
                    .OrderBy(e => e.DeviceId)
                    .ToListAsync(ct);
                return (IReadOnlyList<EdgeUnit>)entities.Select(MapToModel).ToArray();
            },
            cancellationToken);

    /// <remarks>
    /// The load, the reconciliation, and the write all happen inside one
    /// <see cref="GreenhouseDatabase.ExecuteAsync{T}"/> call, so nothing can interleave between
    /// reading the unit and writing the result — in particular not a mapping update, which would
    /// otherwise be reverted by a heartbeat that read the row before it landed.
    /// </remarks>
    public Task<HeartbeatOutcome> RecordHeartbeatAsync(
        HeartbeatMessage heartbeat,
        DateTime receivedAt,
        CancellationToken cancellationToken = default) =>
        _database.ExecuteAsync(
            async (context, ct) =>
            {
                var entity = await context.EdgeUnits
                    .Include(e => e.Slots)
                    .FirstOrDefaultAsync(e => e.DeviceId == heartbeat.DeviceId, ct);

                var outcome = HeartbeatReconciliation.Reconcile(
                    entity is null ? null : MapToModel(entity),
                    heartbeat,
                    receivedAt);

                if (entity is null)
                {
                    entity = new EdgeUnitEntity { DeviceId = outcome.Unit.DeviceId };
                    context.EdgeUnits.Add(entity);
                }

                Apply(context, entity, outcome.Unit);

                await context.SaveChangesAsync(ct);
                return outcome;
            },
            cancellationToken);

    public Task<EdgeUnit?> UpdateMappingAsync(
        string deviceId,
        EdgeUnitMapping mapping,
        CancellationToken cancellationToken = default) =>
        _database.ExecuteAsync(
            async (context, ct) =>
            {
                var entity = await context.EdgeUnits
                    .Include(e => e.Slots)
                    .FirstOrDefaultAsync(e => e.DeviceId == deviceId, ct);

                if (entity is null)
                {
                    return null;
                }

                entity.UnitName = mapping.UnitName;
                entity.Location = mapping.Location;
                entity.MappingVersion += 1;
                entity.MappingStatus = MappingStatuses.PublishPending;

                foreach (var assignment in mapping.Slots)
                {
                    var slot = entity.Slots.FirstOrDefault(s => s.SlotId == assignment.SlotId);
                    if (slot is null)
                    {
                        continue;
                    }

                    slot.Role = assignment.Role;
                    slot.Capability = assignment.Capability;
                    slot.Label = assignment.Label;
                }

                await context.SaveChangesAsync(ct);
                return MapToModel(entity);
            },
            cancellationToken);

    public Task UpdateMappingStatusAsync(
        string deviceId,
        string mappingStatus,
        bool clearTopologyDrift,
        CancellationToken cancellationToken = default) =>
        _database.ExecuteAsync(
            async (context, ct) =>
            {
                var entity = await context.EdgeUnits.FirstOrDefaultAsync(e => e.DeviceId == deviceId, ct);
                if (entity is null)
                {
                    return;
                }

                entity.MappingStatus = mappingStatus;
                if (clearTopologyDrift)
                {
                    entity.TopologyDriftDetectedAt = null;
                }

                await context.SaveChangesAsync(ct);
            },
            cancellationToken);

    private static IQueryable<EdgeUnitEntity> QueryWithSlots(GreenhouseDbContext context) =>
        context.EdgeUnits.AsNoTracking().Include(e => e.Slots);

    /// <summary>Writes <paramref name="unit"/> onto <paramref name="entity"/>, topology included.</summary>
    private static void Apply(GreenhouseDbContext context, EdgeUnitEntity entity, EdgeUnit unit)
    {
        entity.AdvertisedName = unit.AdvertisedName;
        entity.UnitName = unit.UnitName;
        entity.Location = unit.Location;
        entity.MappingVersion = unit.MappingVersion;
        entity.MappingStatus = unit.MappingStatus;
        entity.FirstSeenAt = unit.FirstSeenAt;
        entity.LastHeartbeatAt = unit.LastHeartbeatAt;
        entity.TopologyDriftDetectedAt = unit.TopologyDriftDetectedAt;

        ReplaceSlots(context, entity, unit.Slots);
    }

    /// <summary>
    /// Replaces the stored topology with <paramref name="slots"/>, keeping rows whose slot id is
    /// still present so their primary keys — and any assignment written against them — survive.
    /// </summary>
    private static void ReplaceSlots(
        GreenhouseDbContext context,
        EdgeUnitEntity entity,
        IReadOnlyList<EdgeUnitSlot> slots)
    {
        var incoming = slots.ToDictionary(slot => slot.SlotId);

        foreach (var existing in entity.Slots.ToList())
        {
            if (!incoming.ContainsKey(existing.SlotId))
            {
                context.SlotTopologies.Remove(existing);
                entity.Slots.Remove(existing);
            }
        }

        foreach (var slot in slots)
        {
            var target = entity.Slots.FirstOrDefault(s => s.SlotId == slot.SlotId);
            if (target is null)
            {
                target = new SlotTopologyEntity { SlotId = slot.SlotId };
                entity.Slots.Add(target);
            }

            target.I2cAddress = slot.I2cAddress;
            target.Role = slot.Role;
            target.Capability = slot.Capability;
            target.Label = slot.Label;
            target.ObservedAt = slot.ObservedAt;
        }
    }

    private static EdgeUnit MapToModel(EdgeUnitEntity entity) => new(
        entity.DeviceId,
        entity.AdvertisedName,
        entity.UnitName,
        entity.Location,
        entity.MappingVersion,
        entity.MappingStatus,
        entity.FirstSeenAt,
        entity.LastHeartbeatAt,
        entity.TopologyDriftDetectedAt,
        entity.Slots
            .OrderBy(slot => slot.SlotId)
            .Select(slot => new EdgeUnitSlot(
                slot.SlotId,
                slot.I2cAddress,
                slot.Role,
                slot.Capability,
                slot.Label,
                slot.ObservedAt))
            .ToArray());
}
