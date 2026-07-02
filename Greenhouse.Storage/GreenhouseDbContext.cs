using Greenhouse.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace Greenhouse.Storage;

/// <summary>
/// EF Core context for Main Unit persistence. The context is public so the host
/// composition root can register it via <c>AddDbContext</c>; the entities it maps
/// remain internal to <c>Greenhouse.Storage</c>.
/// </summary>
public class GreenhouseDbContext : DbContext
{
    public GreenhouseDbContext(DbContextOptions<GreenhouseDbContext> options)
        : base(options)
    {
    }

    internal DbSet<MainConfigEntity> MainConfigs => Set<MainConfigEntity>();

    internal DbSet<WifiCredentialsEntity> WifiCredentials => Set<WifiCredentialsEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MainConfigEntity>(entity =>
        {
            entity.ToTable("MainConfigs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.GreenhouseName).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Location).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Description).HasMaxLength(100);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();
        });

        modelBuilder.Entity<WifiCredentialsEntity>(entity =>
        {
            entity.ToTable("WifiCredentials");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.NetworkName).IsRequired().HasMaxLength(100);
            entity.Property(e => e.Password).IsRequired();
            entity.Property(e => e.SavedAt).IsRequired();
        });
    }
}
