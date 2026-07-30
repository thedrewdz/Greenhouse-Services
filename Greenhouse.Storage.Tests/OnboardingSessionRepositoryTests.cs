using Greenhouse.Core.Onboarding;
using Greenhouse.Storage.Repositories;

namespace Greenhouse.Storage.Tests;

public class OnboardingSessionRepositoryTests
{
    private static readonly DateTime StartedAt = new(2026, 7, 1, 22, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetCurrentAsync_returns_null_when_no_session_has_been_started()
    {
        using var db = new SqliteTestDatabase();

        Assert.Null(await new OnboardingSessionRepository(db.Database).GetCurrentAsync());
    }

    [Fact]
    public async Task SaveAsync_inserts_then_GetCurrentAsync_maps_fields()
    {
        using var db = new SqliteTestDatabase();
        var repository = new OnboardingSessionRepository(db.Database);

        await repository.SaveAsync(new OnboardingSession(
            OnboardingStatuses.Scanning,
            SelectedDeviceId: null,
            StartedAt,
            StartedAt));

        var loaded = await repository.GetCurrentAsync();

        Assert.NotNull(loaded);
        Assert.Equal(OnboardingStatuses.Scanning, loaded!.Status);
        Assert.Null(loaded.SelectedDeviceId);
        Assert.Equal(StartedAt, loaded.StartedAt);
    }

    [Fact]
    public async Task SaveAsync_upserts_so_the_store_stays_single_row()
    {
        using var db = new SqliteTestDatabase();
        var repository = new OnboardingSessionRepository(db.Database);

        await repository.SaveAsync(new OnboardingSession(
            OnboardingStatuses.Scanning, null, StartedAt, StartedAt));
        await repository.SaveAsync(new OnboardingSession(
            OnboardingStatuses.Provisioning, "1ADD5912AF61", StartedAt, StartedAt.AddSeconds(5)));

        Assert.Equal(1, db.CountRows("OnboardingSessions"));
        var loaded = await repository.GetCurrentAsync();
        Assert.Equal(OnboardingStatuses.Provisioning, loaded!.Status);
        Assert.Equal("1ADD5912AF61", loaded.SelectedDeviceId);
        Assert.Equal(StartedAt.AddSeconds(5), loaded.UpdatedAt);
    }

    [Fact]
    public async Task ClearAsync_removes_the_session()
    {
        using var db = new SqliteTestDatabase();
        var repository = new OnboardingSessionRepository(db.Database);
        await repository.SaveAsync(new OnboardingSession(
            OnboardingStatuses.Scanning, null, StartedAt, StartedAt));

        await repository.ClearAsync();

        Assert.Equal(0, db.CountRows("OnboardingSessions"));
        Assert.Null(await repository.GetCurrentAsync());
    }

    [Fact]
    public async Task ClearAsync_is_a_no_op_when_there_is_no_session()
    {
        using var db = new SqliteTestDatabase();

        await new OnboardingSessionRepository(db.Database).ClearAsync();

        Assert.Equal(0, db.CountRows("OnboardingSessions"));
    }
}
