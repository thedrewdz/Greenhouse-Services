using Greenhouse.Core.Configuration;
using Greenhouse.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace Greenhouse.Storage.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IWifiCredentialsRepository"/> over the single-row
/// <c>WifiCredentials</c> table. <see cref="SaveAsync"/> upserts so at most one row exists.
/// The <c>SavedAt</c> audit timestamp is a persistence concern stamped here; it is not part
/// of the application model.
/// </summary>
public sealed class WifiCredentialsRepository : IWifiCredentialsRepository
{
    private readonly GreenhouseDbContext _context;

    public WifiCredentialsRepository(GreenhouseDbContext context)
    {
        _context = context;
    }

    public async Task<WifiCredentials?> GetAsync()
    {
        var entity = await _context.WifiCredentials.AsNoTracking().FirstOrDefaultAsync();
        return entity is null ? null : new WifiCredentials(entity.NetworkName, entity.Password);
    }

    public async Task SaveAsync(WifiCredentials credentials)
    {
        var entity = await _context.WifiCredentials.FirstOrDefaultAsync();
        if (entity is null)
        {
            _context.WifiCredentials.Add(new WifiCredentialsEntity
            {
                NetworkName = credentials.NetworkName,
                Password = credentials.Password,
                SavedAt = DateTime.UtcNow,
            });
        }
        else
        {
            entity.NetworkName = credentials.NetworkName;
            entity.Password = credentials.Password;
            entity.SavedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
    }

    public async Task DeleteAsync()
    {
        var entity = await _context.WifiCredentials.FirstOrDefaultAsync();
        if (entity is null)
        {
            return;
        }

        _context.WifiCredentials.Remove(entity);
        await _context.SaveChangesAsync();
    }
}
