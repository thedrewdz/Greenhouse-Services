using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Greenhouse.Storage.Tests;

/// <summary>
/// A migrated, in-memory SQLite database for repository tests. The connection is held
/// open for the lifetime of the fixture so the database survives between contexts; each
/// call to <see cref="CreateContext"/> returns a fresh context sharing that connection,
/// which avoids the EF identity map masking round-trip mapping behavior.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<GreenhouseDbContext> _options;

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<GreenhouseDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = CreateContext();
        context.Database.Migrate();

        Database = new GreenhouseDatabase(_options);
    }

    /// <summary>The seam repositories take, wired to this fixture's database.</summary>
    public GreenhouseDatabase Database { get; }

    public GreenhouseDbContext CreateContext() => new(_options);

    public long CountRows(string table)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        Database.Dispose();
        _connection.Dispose();
    }
}
