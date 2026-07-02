namespace Greenhouse.Core.Configuration;

/// <summary>
/// Application-layer model for stored WiFi credentials. Persisted on a successful
/// network connection and read by the Edge Unit onboarding flow when building the
/// BLE provisioning payload. The password must never appear in API responses or logs.
/// </summary>
public sealed record WifiCredentials(
    string NetworkName,
    string Password);
