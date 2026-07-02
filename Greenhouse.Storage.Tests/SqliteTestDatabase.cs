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

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.Migrate();
    }

    public GreenhouseDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<GreenhouseDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new GreenhouseDbContext(options);
    }

    public long CountRows(string table)
    {
        using var command = _connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)command.ExecuteScalar()!;
    }

    public void Dispose() => _connection.Dispose();
}
