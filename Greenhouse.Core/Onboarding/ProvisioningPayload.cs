namespace Greenhouse.Core.Onboarding;

/// <summary>
/// The provisioning values delivered to an Edge Unit during onboarding. Maps to the BLE
/// provisioning JSON schema in <c>specs/edge-unit-onboarding/spec.md</c>; the mapping to that
/// wire schema is owned entirely by the infrastructure adapter, not by application code.
/// </summary>
/// <param name="DeviceId">Edge Unit hardware identity; must match the advertising unit.</param>
/// <param name="WifiSsid">Target WiFi network name.</param>
/// <param name="WifiPassword">WiFi password (may be empty for open networks). Never logged.</param>
/// <param name="MqttBrokerUri">Bootstrap MQTT broker URI (e.g. <c>mqtt://192.168.1.50</c>).</param>
/// <param name="HeartbeatIntervalMs">Optional heartbeat interval; the Edge Unit defaults to 30000 when omitted.</param>
public sealed record ProvisioningPayload(
    string DeviceId,
    string WifiSsid,
    string WifiPassword,
    string MqttBrokerUri,
    int? HeartbeatIntervalMs = null);
