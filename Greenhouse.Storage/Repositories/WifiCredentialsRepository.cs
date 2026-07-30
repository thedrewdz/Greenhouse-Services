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
    private readonly GreenhouseDatabase _database;

    public WifiCredentialsRepository(GreenhouseDatabase database)
    {
        _database = database;
    }

    public Task<WifiCredentials?> GetAsync() =>
        _database.ExecuteAsync(async (context, ct) =>
        {
            var entity = await context.WifiCredentials.AsNoTracking().FirstOrDefaultAsync(ct);
            return entity is null ? null : new WifiCredentials(entity.NetworkName, entity.Password);
        });

    public Task SaveAsync(WifiCredentials credentials) =>
        _database.ExecuteAsync(async (context, ct) =>
        {
            var entity = await context.WifiCredentials.FirstOrDefaultAsync(ct);
            if (entity is null)
            {
                context.WifiCredentials.Add(new WifiCredentialsEntity
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

            await context.SaveChangesAsync(ct);
        });

    public Task DeleteAsync() =>
        _database.ExecuteAsync(async (context, ct) =>
        {
            var entity = await context.WifiCredentials.FirstOrDefaultAsync(ct);
            if (entity is null)
            {
                return;
            }

            context.WifiCredentials.Remove(entity);
            await context.SaveChangesAsync(ct);
        });
}
