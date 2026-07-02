namespace Greenhouse.Core.Networking;

/// <summary>
/// Outcome of a WiFi connection attempt. A closed hierarchy: exactly one of
/// <see cref="Connected"/>, <see cref="Failed"/>, or <see cref="TimedOut"/>.
/// </summary>
public abstract record ConnectResult
{
    private ConnectResult()
    {
    }

    /// <summary>The Main Unit connected to the network successfully.</summary>
    public sealed record Connected : ConnectResult;

    /// <summary>The attempt failed (for example, authentication failure).</summary>
    public sealed record Failed(string ErrorMessage) : ConnectResult;

    /// <summary>The attempt did not complete within the connector's timeout.</summary>
    public sealed record TimedOut : ConnectResult;
}
