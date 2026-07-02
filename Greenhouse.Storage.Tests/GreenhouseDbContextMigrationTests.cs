using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Greenhouse.Storage.Tests;

/// <summary>
/// Verifies the InitialCreate migration against a real (in-memory) SQLite database:
/// it creates both single-row tables and reverts cleanly.
/// </summary>
public class GreenhouseDbContextMigrationTests
{
    private static GreenhouseDbContext CreateContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<GreenhouseDbContext>()
            .UseSqlite(connection)
            .Options;

        return new GreenhouseDbContext(options);
    }

    private static IReadOnlyList<string> GetTableNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";

        var names = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    [Fact]
    public void Migration_creates_MainConfigs_and_WifiCredentials_tables()
    {
        // Keep the connection open so the in-memory database survives for the test.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var context = CreateContext(connection);

        context.Database.Migrate();

        var tables = GetTableNames(connection);
        Assert.Contains("MainConfigs", tables);
        Assert.Contains("WifiCredentials", tables);
    }

    [Fact]
    public void Migration_is_reversible()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var context = CreateContext(connection);

        context.Database.Migrate();

        var migrator = context.GetInfrastructure().GetRequiredService<IMigrator>();
        migrator.Migrate(Migration.InitialDatabase);

        var tables = GetTableNames(connection);
        Assert.DoesNotContain("MainConfigs", tables);
        Assert.DoesNotContain("WifiCredentials", tables);
    }
}
