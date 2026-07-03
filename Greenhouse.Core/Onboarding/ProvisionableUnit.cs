namespace Greenhouse.Core.Onboarding;

/// <summary>
/// An Edge Unit discovered in Provisioning Mode and available for onboarding. Expressed in
/// Greenhouse domain terms — no transport (BLE) types.
/// </summary>
/// <param name="DeviceId">Opaque transport device identifier used to target the unit.</param>
/// <param name="AdvertisedName">The name the unit advertised (e.g. <c>GH-Edge-1ADD5912AF61</c>).</param>
public sealed record ProvisionableUnit(string DeviceId, string AdvertisedName);
