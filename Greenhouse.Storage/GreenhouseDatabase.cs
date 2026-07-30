using Microsoft.EntityFrameworkCore;

namespace Greenhouse.Storage;

/// <summary>
/// The single entry point every repository uses to reach the Main Unit database. It owns a
/// short-lived <see cref="GreenhouseDbContext"/> per operation and serialises operations against
/// one another.
/// </summary>
/// <remarks>
/// <para>
/// Serialisation is required, not defensive: the host keeps one SQLite connection open for the
/// process lifetime (so EF's migration executor sees schema changes reliably on ARM64), and a
/// SQLite connection can only run one command at a time. Background work — heartbeat ingestion
/// and configuration publishing — now writes outside any request, so without this gate a
/// background write could collide with an API read.
/// </para>
/// <para>
/// Because each operation gets a fresh context, repositories are safe to register as singletons
/// and can be injected into long-lived services without capturing a request scope.
/// </para>
/// </remarks>
public sealed class GreenhouseDatabase : IDisposable
{
    private readonly DbContextOptions<GreenhouseDbContext> _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public GreenhouseDatabase(DbContextOptions<GreenhouseDbContext> options)
    {
        _options = options;
    }

    /// <summary>Runs <paramref name="operation"/> against a fresh context and returns its result.</summary>
    public async Task<T> ExecuteAsync<T>(
        Func<GreenhouseDbContext, CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = new GreenhouseDbContext(_options);
            return await operation(context, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Runs <paramref name="operation"/> against a fresh context.</summary>
    public async Task ExecuteAsync(
        Func<GreenhouseDbContext, CancellationToken, Task> operation,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await using var context = new GreenhouseDbContext(_options);
            await operation(context, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
