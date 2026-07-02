using System.Diagnostics;
using Greenhouse.Core.Networking;

namespace Greenhouse.Network;

/// <summary>
/// Runtime <see cref="INmcliCommandRunner"/> that shells out to the <c>nmcli</c> binary on
/// Raspberry Pi Debian Bookworm. If the binary cannot be started, it surfaces a
/// <see cref="NetworkConnectorUnavailableException"/> so callers can map it to a 503.
/// </summary>
internal sealed class ProcessNmcliCommandRunner : INmcliCommandRunner
{
    private const string Executable = "nmcli";

    public async Task<NmcliResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            if (!process.Start())
            {
                throw new NetworkConnectorUnavailableException("Failed to start the nmcli process.");
            }
        }
        catch (Exception ex) when (ex is not NetworkConnectorUnavailableException)
        {
            throw new NetworkConnectorUnavailableException("nmcli is not available on this host.", ex);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;

        return new NmcliResult(process.ExitCode, standardOutput, standardError);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort: the process may have exited between the check and the kill.
        }
    }
}
