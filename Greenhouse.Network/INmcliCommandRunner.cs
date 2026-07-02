namespace Greenhouse.Network;

/// <summary>
/// Seam over the <c>nmcli</c> subprocess so the adapter's argument construction and output
/// parsing are unit-testable without a real network manager. The runtime implementation
/// spawns the process; tests substitute a fake.
/// </summary>
public interface INmcliCommandRunner
{
    Task<NmcliResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

/// <summary>Captured result of an <c>nmcli</c> invocation.</summary>
public sealed record NmcliResult(int ExitCode, string StandardOutput, string StandardError);
