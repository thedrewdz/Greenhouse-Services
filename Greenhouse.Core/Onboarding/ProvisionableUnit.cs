namespace Greenhouse.Core.Onboarding;

/// <summary>
/// An Edge Unit discovered in Provisioning Mode and available for onboarding. Expressed in
/// Greenhouse domain terms — no transport (BLE) types.
/// </summary>
/// <param name="DeviceId">
/// Edge Unit hardware identity (WiFi MAC address, e.g. <c>1ADD5912AF61</c>). This is the
/// identifier the API, the hub, and every later heartbeat use; the transport adapter derives it
/// from the advertised name.
/// </param>
/// <param name="TransportAddress">
/// Opaque handle the transport uses to target this unit (a BlueZ address for the BLE adapter).
/// Application code never interprets it — it passes the unit back to the transport unchanged.
/// </param>
/// <param name="AdvertisedName">The name the unit advertised (e.g. <c>GH-Edge-1ADD5912AF61</c>).</param>
/// <param name="Rssi">
/// Signal strength of the most recent advertisement, or <c>null</c> when the transport has not
/// reported one yet. Surfaced to the operator as signal quality when choosing a candidate.
/// </param>
public sealed record ProvisionableUnit(
    string DeviceId,
    string TransportAddress,
    string AdvertisedName,
    int? Rssi = null);
