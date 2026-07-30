using Greenhouse.Core.Networking;
using Microsoft.Extensions.Logging;

namespace Greenhouse.Network;

/// <summary>
/// <see cref="INetworkConnector"/> backed by the <c>nmcli</c> subprocess. Builds nmcli
/// arguments, enforces a bounded connection timeout, and parses nmcli output into
/// application results. The WiFi password is never written to a log or included in a
/// returned error message.
/// </summary>
public sealed class NmcliNetworkAdapter : INetworkConnector
{
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(15);

    private readonly INmcliCommandRunner _runner;
    private readonly ILogger<NmcliNetworkAdapter> _logger;
    private readonly TimeSpan _connectTimeout;

    public NmcliNetworkAdapter(
        INmcliCommandRunner runner,
        ILogger<NmcliNetworkAdapter> logger,
        TimeSpan? connectTimeout = null)
    {
        _runner = runner;
        _logger = logger;
        _connectTimeout = connectTimeout ?? DefaultConnectTimeout;
    }

    public async Task<bool> IsOnlineAsync(CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(new[] { "networking", "connectivity" }, cancellationToken);
        return string.Equals(result.StandardOutput.Trim(), "full", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<string?> GetCurrentNetworkNameAsync(CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(new[] { "-t", "-f", "ACTIVE,SSID", "dev", "wifi" }, cancellationToken);

        using var reader = new StringReader(result.StandardOutput);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            // Terse format is "<active>:<ssid>", e.g. "yes:MyNetwork".
            var separator = line.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            if (string.Equals(line[..separator], "yes", StringComparison.OrdinalIgnoreCase))
            {
                var ssid = line[(separator + 1)..];
                return string.IsNullOrEmpty(ssid) ? null : ssid;
            }
        }

        return null;
    }

    public async Task<string?> GetLocalAddressAsync(CancellationToken cancellationToken = default)
    {
        // Terse output is one "IP4.ADDRESS[n]:<addr>/<prefix>" line per configured address,
        // grouped per device. Devices with no IPv4 lease simply contribute no lines.
        var result = await _runner.RunAsync(
            new[] { "-t", "-f", "IP4.ADDRESS", "device", "show" },
            cancellationToken);

        using var reader = new StringReader(result.StandardOutput);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var separator = line.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var value = line[(separator + 1)..].Trim();

            // Strip the CIDR suffix; nmcli reports "192.168.1.50/24".
            var slash = value.IndexOf('/');
            if (slash >= 0)
            {
                value = value[..slash];
            }

            if (IsRoutableIpv4(value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Accepts only dotted-quad IPv4 addresses an Edge Unit could actually reach the broker on:
    /// loopback (127.x) and link-local (169.254.x) are rejected.
    /// </summary>
    private static bool IsRoutableIpv4(string value)
    {
        var octets = value.Split('.');
        if (octets.Length != 4)
        {
            return false;
        }

        foreach (var octet in octets)
        {
            if (!byte.TryParse(octet, out _))
            {
                return false;
            }
        }

        return !value.StartsWith("127.", StringComparison.Ordinal)
               && !value.StartsWith("169.254.", StringComparison.Ordinal);
    }

    public async Task<ConnectResult> ConnectAsync(
        string networkName,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var arguments = new List<string> { "dev", "wifi", "connect", networkName };
        if (!string.IsNullOrEmpty(password))
        {
            arguments.Add("password");
            arguments.Add(password);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_connectTimeout);

        NmcliResult result;
        try
        {
            result = await _runner.RunAsync(arguments, timeoutSource.Token);
        }
        catch (OperationCanceledException)
            when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "WiFi connection to {NetworkName} timed out after {TimeoutSeconds}s.",
                networkName,
                _connectTimeout.TotalSeconds);
            return new ConnectResult.TimedOut();
        }

        if (result.ExitCode == 0)
        {
            return new ConnectResult.Connected();
        }

        var message = Redact(BuildErrorMessage(result.StandardError), password);
        _logger.LogWarning("WiFi connection to {NetworkName} failed: {Error}", networkName, message);
        return new ConnectResult.Failed(message);
    }

    private static string BuildErrorMessage(string standardError)
    {
        var trimmed = standardError.Trim();
        return string.IsNullOrEmpty(trimmed) ? "WiFi connection failed." : trimmed;
    }

    // Defensive: ensure the password can never survive in a message even if nmcli echoes it.
    private static string Redact(string message, string? password) =>
        string.IsNullOrEmpty(password) ? message : message.Replace(password, "***", StringComparison.Ordinal);
}
