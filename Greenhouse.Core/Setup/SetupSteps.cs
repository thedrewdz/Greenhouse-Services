namespace Greenhouse.Core.Setup;

/// <summary>Canonical <c>requiredStep</c> values produced by <see cref="ReadSetupStatus"/>.</summary>
public static class SetupSteps
{
    public const string NetworkConnection = "network-connection";
    public const string MainConfig = "main-config";
    public const string NetworkRecovery = "network-recovery";
}
