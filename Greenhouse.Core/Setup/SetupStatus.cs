namespace Greenhouse.Core.Setup;

/// <summary>
/// Read model describing where the Main Unit is in the first-run flow.
/// <paramref name="RequiredStep"/> is one of <see cref="SetupSteps"/> or <c>null</c> when setup is complete.
/// </summary>
public sealed record SetupStatus(bool SetupComplete, bool IsOnline, string? RequiredStep);
