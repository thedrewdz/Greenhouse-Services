namespace Greenhouse.Core.Networking;

/// <summary>
/// Thrown by an <see cref="INetworkConnector"/> implementation when the underlying OS
/// network manager cannot be reached (for example, the CLI tool is missing or the process
/// cannot be started). Callers translate this to a 503 at the API boundary.
/// </summary>
public sealed class NetworkConnectorUnavailableException : Exception
{
    public NetworkConnectorUnavailableException(string message)
        : base(message)
    {
    }

    public NetworkConnectorUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
