namespace Greenhouse.Core.Networking;

/// <summary>
/// Application-layer port for OS-level WiFi connectivity. The concrete adapter lives in
/// an infrastructure project and shells out to the OS network manager. Application code
/// depends only on this interface.
/// </summary>
public interface INetworkConnector
{
    /// <summary>Returns <c>true</c> when the Main Unit has an active default route.</summary>
    Task<bool> IsOnlineAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the currently connected network name, or <c>null</c> when offline.</summary>
    Task<string?> GetCurrentNetworkNameAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the Main Unit's primary local IPv4 address (e.g. <c>192.168.1.50</c>), or
    /// <c>null</c> when the unit has no usable address. Edge Unit onboarding derives the
    /// bootstrap <c>mqtt_broker_uri</c> from this, so loopback and link-local addresses are
    /// never returned — an Edge Unit could not reach the broker on them.
    /// </summary>
    Task<string?> GetLocalAddressAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to connect to <paramref name="networkName"/>. Enforces a bounded timeout
    /// and never blocks indefinitely. The password must never be logged.
    /// </summary>
    Task<ConnectResult> ConnectAsync(
        string networkName,
        string? password,
        CancellationToken cancellationToken = default);
}
