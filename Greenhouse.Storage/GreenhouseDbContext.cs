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

    internal DbSet<EdgeUnitEntity> EdgeUnits => Set<EdgeUnitEntity>();

    internal DbSet<SlotTopologyEntity> SlotTopologies => Set<SlotTopologyEntity>();

    internal DbSet<OnboardingSessionEntity> OnboardingSessions => Set<OnboardingSessionEntity>();

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

        modelBuilder.Entity<EdgeUnitEntity>(entity =>
        {
            entity.ToTable("EdgeUnits");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DeviceId).IsRequired().HasMaxLength(32);
            // One registration per physical unit: a repeat heartbeat must update, never insert.
            entity.HasIndex(e => e.DeviceId).IsUnique();
            entity.Property(e => e.AdvertisedName).IsRequired().HasMaxLength(64);
            entity.Property(e => e.UnitName).HasMaxLength(100);
            entity.Property(e => e.Location).HasMaxLength(100);
            entity.Property(e => e.MappingVersion).IsRequired();
            entity.Property(e => e.MappingStatus).IsRequired().HasMaxLength(32);
            entity.Property(e => e.FirstSeenAt).IsRequired();

            entity.HasMany(e => e.Slots)
                .WithOne(s => s.EdgeUnit)
                .HasForeignKey(s => s.EdgeUnitId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SlotTopologyEntity>(entity =>
        {
            entity.ToTable("SlotTopologies");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SlotId).IsRequired();
            entity.Property(e => e.I2cAddress).IsRequired().HasMaxLength(8);
            entity.Property(e => e.Role).HasMaxLength(16);
            entity.Property(e => e.Capability).HasMaxLength(32);
            entity.Property(e => e.Label).HasMaxLength(100);
            entity.Property(e => e.ObservedAt).IsRequired();
            entity.HasIndex(e => new { e.EdgeUnitId, e.SlotId }).IsUnique();
        });

        modelBuilder.Entity<OnboardingSessionEntity>(entity =>
        {
            entity.ToTable("OnboardingSessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).IsRequired().HasMaxLength(32);
            entity.Property(e => e.SelectedDeviceId).HasMaxLength(32);
            entity.Property(e => e.StartedAt).IsRequired();
            entity.Property(e => e.UpdatedAt).IsRequired();
        });
    }
}
