namespace Greenhouse.Core.Networking;

/// <summary>
/// Application-layer port for OS-level WiFi connectivity. The concrete adapter lives in
/// an infrastructure project and shells out to the OS network manager. Application code
/// depends only on this interface.
/// </summary>
/// <remarks>
/// <c>GetLocalAddressAsync</c> (from the full spec) is added later by the Edge Unit
/// onboarding epic; this Phase-1 port covers connectivity and connection only.
/// </remarks>
public interface INetworkConnector
{
    /// <summary>Returns <c>true</c> when the Main Unit has an active default route.</summary>
    Task<bool> IsOnlineAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the currently connected network name, or <c>null</c> when offline.</summary>
    Task<string?> GetCurrentNetworkNameAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to connect to <paramref name="networkName"/>. Enforces a bounded timeout
    /// and never blocks indefinitely. The password must never be logged.
    /// </summary>
    Task<ConnectResult> ConnectAsync(
        string networkName,
        string? password,
        CancellationToken cancellationToken = default);
}
