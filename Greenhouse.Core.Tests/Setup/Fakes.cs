using Greenhouse.Core.Configuration;
using Greenhouse.Core.Networking;

namespace Greenhouse.Core.Tests.Setup;

/// <summary>In-memory single-row MainConfig store; counts writes for persistence assertions.</summary>
internal sealed class FakeMainConfigRepository : IMainConfigRepository
{
    public MainConfig? Current { get; set; }
    public int CreateCount { get; private set; }

    public Task<MainConfig?> GetAsync() => Task.FromResult(Current);

    public Task CreateAsync(MainConfig config)
    {
        CreateCount++;
        Current = config;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(MainConfig config)
    {
        Current = config;
        return Task.CompletedTask;
    }

    public Task DeleteAsync()
    {
        Current = null;
        return Task.CompletedTask;
    }
}

/// <summary>In-memory single-row WifiCredentials store; counts saves for persistence assertions.</summary>
internal sealed class FakeWifiCredentialsRepository : IWifiCredentialsRepository
{
    public WifiCredentials? Current { get; set; }
    public int SaveCount { get; private set; }

    public Task<WifiCredentials?> GetAsync() => Task.FromResult(Current);

    public Task SaveAsync(WifiCredentials credentials)
    {
        SaveCount++;
        Current = credentials;
        return Task.CompletedTask;
    }

    public Task DeleteAsync()
    {
        Current = null;
        return Task.CompletedTask;
    }
}

/// <summary>Scriptable network connector that records the arguments it was called with.</summary>
internal sealed class FakeNetworkConnector : INetworkConnector
{
    public bool Online { get; set; }
    public ConnectResult ConnectResult { get; set; } = new ConnectResult.Connected();
    public string? LastNetworkName { get; private set; }
    public string? LastPassword { get; private set; }

    public Task<bool> IsOnlineAsync(CancellationToken cancellationToken = default) => Task.FromResult(Online);

    public Task<string?> GetCurrentNetworkNameAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(Online ? "MyNetwork" : null);

    public Task<ConnectResult> ConnectAsync(
        string networkName,
        string? password,
        CancellationToken cancellationToken = default)
    {
        LastNetworkName = networkName;
        LastPassword = password;
        return Task.FromResult(ConnectResult);
    }
}

/// <summary>Fixed-clock <see cref="TimeProvider"/> for deterministic timestamp assertions.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;
}
