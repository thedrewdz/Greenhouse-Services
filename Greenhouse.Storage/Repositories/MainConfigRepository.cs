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
    private readonly GreenhouseDbContext _context;

    public MainConfigRepository(GreenhouseDbContext context)
    {
        _context = context;
    }

    public async Task<MainConfig?> GetAsync()
    {
        var entity = await _context.MainConfigs.AsNoTracking().FirstOrDefaultAsync();
        return entity is null ? null : MapToModel(entity);
    }

    public async Task CreateAsync(MainConfig config)
    {
        _context.MainConfigs.Add(new MainConfigEntity
        {
            GreenhouseName = config.GreenhouseName,
            Location = config.Location,
            Description = config.Description,
            CreatedAt = config.CreatedAt,
            UpdatedAt = config.UpdatedAt,
        });

        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(MainConfig config)
    {
        var entity = await _context.MainConfigs.FirstOrDefaultAsync();
        if (entity is null)
        {
            return;
        }

        entity.GreenhouseName = config.GreenhouseName;
        entity.Location = config.Location;
        entity.Description = config.Description;
        entity.UpdatedAt = config.UpdatedAt;

        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync()
    {
        var entity = await _context.MainConfigs.FirstOrDefaultAsync();
        if (entity is null)
        {
            return;
        }

        _context.MainConfigs.Remove(entity);
        await _context.SaveChangesAsync();
    }

    private static MainConfig MapToModel(MainConfigEntity entity) => new(
        entity.GreenhouseName,
        entity.Location,
        entity.Description,
        entity.CreatedAt,
        entity.UpdatedAt);
}
