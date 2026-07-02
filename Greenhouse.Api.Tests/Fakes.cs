using Greenhouse.Core.Configuration;
using Greenhouse.Core.Networking;

namespace Greenhouse.Api.Tests;

internal sealed class FakeMainConfigRepository : IMainConfigRepository
{
    public MainConfig? Current { get; set; }

    public Task<MainConfig?> GetAsync() => Task.FromResult(Current);

    public Task CreateAsync(MainConfig config)
    {
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

internal sealed class FakeWifiCredentialsRepository : IWifiCredentialsRepository
{
    public WifiCredentials? Current { get; set; }

    public Task<WifiCredentials?> GetAsync() => Task.FromResult(Current);

    public Task SaveAsync(WifiCredentials credentials)
    {
        Current = credentials;
        return Task.CompletedTask;
    }

    public Task DeleteAsync()
    {
        Current = null;
        return Task.CompletedTask;
    }
}

internal sealed class FakeNetworkConnector : INetworkConnector
{
    public bool Online { get; set; }
    public string? CurrentNetworkName { get; set; }
    public ConnectResult ConnectResult { get; set; } = new ConnectResult.Connected();
    public bool ThrowUnavailable { get; set; }

    public Task<bool> IsOnlineAsync(CancellationToken cancellationToken = default)
    {
        if (ThrowUnavailable)
        {
            throw new NetworkConnectorUnavailableException("unavailable");
        }

        return Task.FromResult(Online);
    }

    public Task<string?> GetCurrentNetworkNameAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CurrentNetworkName);

    public Task<ConnectResult> ConnectAsync(
        string networkName,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (ThrowUnavailable)
        {
            throw new NetworkConnectorUnavailableException("unavailable");
        }

        return Task.FromResult(ConnectResult);
    }
}
