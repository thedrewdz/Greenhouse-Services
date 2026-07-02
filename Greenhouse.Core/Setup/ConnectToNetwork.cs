using Greenhouse.Core.Configuration;
using Greenhouse.Core.Networking;

namespace Greenhouse.Core.Setup;

/// <summary>
/// Submits WiFi credentials to the OS connector and, only on success, persists them for the
/// Edge Unit onboarding flow. Never logs or echoes the password.
/// </summary>
public sealed class ConnectToNetwork
{
    private readonly INetworkConnector _connector;
    private readonly IWifiCredentialsRepository _credentials;

    public ConnectToNetwork(INetworkConnector connector, IWifiCredentialsRepository credentials)
    {
        _connector = connector;
        _credentials = credentials;
    }

    public async Task<ConnectResult> ExecuteAsync(
        string networkName,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var trimmedName = networkName?.Trim() ?? string.Empty;

        var result = await _connector.ConnectAsync(trimmedName, password, cancellationToken);
        if (result is ConnectResult.Connected)
        {
            await _credentials.SaveAsync(new WifiCredentials(trimmedName, password ?? string.Empty));
        }

        return result;
    }
}
