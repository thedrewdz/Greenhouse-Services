using Greenhouse.Core.Configuration;
using Greenhouse.Storage.Repositories;

namespace Greenhouse.Storage.Tests;

public class MainConfigRepositoryTests
{
    [Fact]
    public async Task GetAsync_returns_null_when_table_is_empty()
    {
        using var db = new SqliteTestDatabase();
        var repository = new MainConfigRepository(db.Database);

        Assert.Null(await repository.GetAsync());
    }

    [Fact]
    public async Task CreateAsync_then_GetAsync_round_trips_all_fields()
    {
        using var db = new SqliteTestDatabase();
        var created = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);
        var config = new MainConfig("North Greenhouse", "Block A", "Main production", created, created);

        await new MainConfigRepository(db.Database).CreateAsync(config);
        var loaded = await new MainConfigRepository(db.Database).GetAsync();

        Assert.NotNull(loaded);
        Assert.Equal("North Greenhouse", loaded!.GreenhouseName);
        Assert.Equal("Block A", loaded.Location);
        Assert.Equal("Main production", loaded.Description);
        Assert.Equal(created, loaded.CreatedAt);
        Assert.Equal(created, loaded.UpdatedAt);
    }

    [Fact]
    public async Task CreateAsync_persists_null_description()
    {
        using var db = new SqliteTestDatabase();
        var created = new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc);

        await new MainConfigRepository(db.Database)
            .CreateAsync(new MainConfig("North", "Block A", null, created, created));
        var loaded = await new MainConfigRepository(db.Database).GetAsync();

        Assert.NotNull(loaded);
        Assert.Null(loaded!.Description);
    }
}
