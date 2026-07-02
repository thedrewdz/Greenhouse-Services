using Greenhouse.Core.Configuration;
using Greenhouse.Storage.Repositories;

namespace Greenhouse.Storage.Tests;

public class WifiCredentialsRepositoryTests
{
    [Fact]
    public async Task GetAsync_returns_null_when_table_is_empty()
    {
        using var db = new SqliteTestDatabase();
        var repository = new WifiCredentialsRepository(db.CreateContext());

        Assert.Null(await repository.GetAsync());
    }

    [Fact]
    public async Task SaveAsync_inserts_then_GetAsync_maps_fields()
    {
        using var db = new SqliteTestDatabase();

        await new WifiCredentialsRepository(db.CreateContext())
            .SaveAsync(new WifiCredentials("MyNetwork", "secret"));
        var loaded = await new WifiCredentialsRepository(db.CreateContext()).GetAsync();

        Assert.NotNull(loaded);
        Assert.Equal("MyNetwork", loaded!.NetworkName);
        Assert.Equal("secret", loaded.Password);
    }

    [Fact]
    public async Task SaveAsync_upserts_replacing_the_existing_row()
    {
        using var db = new SqliteTestDatabase();

        await new WifiCredentialsRepository(db.CreateContext())
            .SaveAsync(new WifiCredentials("FirstNetwork", "first-pass"));
        await new WifiCredentialsRepository(db.CreateContext())
            .SaveAsync(new WifiCredentials("SecondNetwork", "second-pass"));

        var loaded = await new WifiCredentialsRepository(db.CreateContext()).GetAsync();

        Assert.Equal(1, db.CountRows("WifiCredentials"));
        Assert.NotNull(loaded);
        Assert.Equal("SecondNetwork", loaded!.NetworkName);
        Assert.Equal("second-pass", loaded.Password);
    }
}
