namespace Greenhouse.Core.Onboarding;

/// <summary>
/// Canonical onboarding <c>status</c> values. The same literals appear in
/// <c>GET /api/onboarding</c>, in the <c>OnboardingStateChanged</c> hub event, and in the
/// persisted session row (<c>specs/edge-unit-configuration/spec.md</c>).
/// </summary>
public static class OnboardingStatuses
{
    /// <summary>No active session.</summary>
    public const string Idle = "idle";

    /// <summary>BLE scan in progress.</summary>
    public const string Scanning = "scanning";

    /// <summary>Scan complete; device list available.</summary>
    public const string CandidatesReady = "candidates-ready";

    /// <summary>Provisioning payload being delivered to the selected device.</summary>
    public const string Provisioning = "provisioning";

    /// <summary>Payload accepted; waiting for the first heartbeat.</summary>
    public const string AwaitingHeartbeat = "awaiting-heartbeat";

    /// <summary>First heartbeat received; runtime mapping needed.</summary>
    public const string MappingRequired = "mapping-required";

    /// <summary>Mapping stored and published to the Edge Unit.</summary>
    public const string Complete = "complete";

    /// <summary>Error — see the accompanying error code and message.</summary>
    public const string Failed = "failed";

    /// <summary>Scan timed out with no candidates.</summary>
    public const string NoDeviceFound = "no-device-found";
}
