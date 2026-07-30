using Greenhouse.Core.Configuration;
using Greenhouse.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace Greenhouse.Storage.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IMainConfigRepository"/> over the single-row
/// <c>MainConfigs</c> table. Maps between the EF entity and the application model at
/// this boundary so no entity type escapes <c>Greenhouse.Storage</c>.
/// </summary>
public sealed class MainConfigRepository : IMainConfigRepository
{
    private readonly GreenhouseDatabase _database;

    public MainConfigRepository(GreenhouseDatabase database)
    {
        _database = database;
    }

    public Task<MainConfig?> GetAsync() =>
        _database.ExecuteAsync(async (context, ct) =>
        {
            var entity = await context.MainConfigs.AsNoTracking().OrderBy(e => e.Id).FirstOrDefaultAsync(ct);
            return entity is null ? null : MapToModel(entity);
        });

    public Task CreateAsync(MainConfig config) =>
        _database.ExecuteAsync(async (context, ct) =>
        {
            context.MainConfigs.Add(new MainConfigEntity
            {
                GreenhouseName = config.GreenhouseName,
                Location = config.Location,
                Description = config.Description,
                CreatedAt = config.CreatedAt,
                UpdatedAt = config.UpdatedAt,
            });

            await context.SaveChangesAsync(ct);
        });

    public Task UpdateAsync(MainConfig config) =>
        _database.ExecuteAsync(async (context, ct) =>
        {
            var entity = await context.MainConfigs.FirstOrDefaultAsync(ct);
            if (entity is null)
            {
                return;
            }

            entity.GreenhouseName = config.GreenhouseName;
            entity.Location = config.Location;
            entity.Description = config.Description;
            entity.UpdatedAt = config.UpdatedAt;

            await context.SaveChangesAsync(ct);
        });

    public Task DeleteAsync() =>
        _database.ExecuteAsync(async (context, ct) =>
        {
            var entity = await context.MainConfigs.FirstOrDefaultAsync(ct);
            if (entity is null)
            {
                return;
            }

            context.MainConfigs.Remove(entity);
            await context.SaveChangesAsync(ct);
        });

    private static MainConfig MapToModel(MainConfigEntity entity) => new(
        entity.GreenhouseName,
        entity.Location,
        entity.Description,
        entity.CreatedAt,
        entity.UpdatedAt);
}
