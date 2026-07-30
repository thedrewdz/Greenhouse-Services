using Greenhouse.Core.Onboarding;
using Greenhouse.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace Greenhouse.Storage.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IOnboardingSessionRepository"/> over the single-row
/// <c>OnboardingSessions</c> table. <see cref="SaveAsync"/> upserts so at most one row exists,
/// which enforces the Phase 1 rule that only one onboarding session is active at a time.
/// </summary>
public sealed class OnboardingSessionRepository : IOnboardingSessionRepository
{
    private readonly GreenhouseDatabase _database;

    public OnboardingSessionRepository(GreenhouseDatabase database)
    {
        _database = database;
    }

    public Task<OnboardingSession?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        _database.ExecuteAsync(
            async (context, ct) =>
            {
                var entity = await context.OnboardingSessions
                    .AsNoTracking()
                    .OrderBy(e => e.Id)
                    .FirstOrDefaultAsync(ct);

                return entity is null
                    ? null
                    : new OnboardingSession(
                        entity.Status,
                        entity.SelectedDeviceId,
                        entity.StartedAt,
                        entity.UpdatedAt);
            },
            cancellationToken);

    public Task SaveAsync(OnboardingSession session, CancellationToken cancellationToken = default) =>
        _database.ExecuteAsync(
            async (context, ct) =>
            {
                var entity = await context.OnboardingSessions.OrderBy(e => e.Id).FirstOrDefaultAsync(ct);
                if (entity is null)
                {
                    entity = new OnboardingSessionEntity();
                    context.OnboardingSessions.Add(entity);
                }

                entity.Status = session.Status;
                entity.SelectedDeviceId = session.SelectedDeviceId;
                entity.StartedAt = session.StartedAt;
                entity.UpdatedAt = session.UpdatedAt;

                await context.SaveChangesAsync(ct);
            },
            cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        _database.ExecuteAsync(
            async (context, ct) =>
            {
                var entities = await context.OnboardingSessions.ToListAsync(ct);
                if (entities.Count == 0)
                {
                    return;
                }

                context.OnboardingSessions.RemoveRange(entities);
                await context.SaveChangesAsync(ct);
            },
            cancellationToken);
}
