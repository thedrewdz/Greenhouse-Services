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

    public Task UpsertAsync(EdgeUnit edgeUnit, CancellationToken cancellationToken = default) =>
        _database.ExecuteAsync(
            async (context, ct) =>
            {
                var entity = await context.EdgeUnits
                    .Include(e => e.Slots)
                    .FirstOrDefaultAsync(e => e.DeviceId == edgeUnit.DeviceId, ct);

                if (entity is null)
                {
                    entity = new EdgeUnitEntity { DeviceId = edgeUnit.DeviceId };
                    context.EdgeUnits.Add(entity);
                }

                entity.AdvertisedName = edgeUnit.AdvertisedName;
                entity.UnitName = edgeUnit.UnitName;
                entity.Location = edgeUnit.Location;
                entity.MappingVersion = edgeUnit.MappingVersion;
                entity.MappingStatus = edgeUnit.MappingStatus;
                entity.FirstSeenAt = edgeUnit.FirstSeenAt;
                entity.LastHeartbeatAt = edgeUnit.LastHeartbeatAt;
                entity.TopologyDriftDetectedAt = edgeUnit.TopologyDriftDetectedAt;

                ReplaceSlots(context, entity, edgeUnit.Slots);

                await context.SaveChangesAsync(ct);
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
