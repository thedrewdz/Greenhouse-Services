namespace Greenhouse.Core.Onboarding;

/// <summary>
/// The onboarding timing constraints from <c>specs/edge-unit-configuration/spec.md</c>.
/// Injected rather than hard-coded so tests can exercise timeout paths without waiting.
/// </summary>
/// <param name="Scan">No-device discovery timeout per scan session.</param>
/// <param name="Heartbeat">
/// Session timeout waiting for the first valid heartbeat, measured from acceptance of the
/// provisioning payload.
/// </param>
public sealed record OnboardingTimeouts(TimeSpan Scan, TimeSpan Heartbeat)
{
    /// <summary>The canonical Phase 1 values: 30-second scan, 90-second heartbeat wait.</summary>
    public static OnboardingTimeouts Default { get; } =
        new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90));
}
