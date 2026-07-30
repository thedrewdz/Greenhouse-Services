namespace Greenhouse.Core.Onboarding;

/// <summary>
/// Application-layer port for Edge Unit onboarding over the provisioning transport. This is the
/// only onboarding-transport contract application use cases may depend on. It expresses
/// operations in Greenhouse domain terms and exposes no BLE-specific types or GATT UUIDs; the
/// concrete adapter (<c>Greenhouse.Bluetooth</c>) owns all transport mechanics.
/// </summary>
public interface IEdgeUnitProvisioningTransport
{
    /// <summary>
    /// Streams Edge Units advertising in Provisioning Mode as they are observed, for up to
    /// <paramref name="timeout"/>. Scanning starts only when enumeration begins — never at
    /// process startup — and stops when the timeout elapses, the enumeration is abandoned, or
    /// <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <remarks>
    /// A unit may be yielded more than once: transports commonly report the advertised name
    /// first and signal strength a moment later. Callers key observations by
    /// <see cref="ProvisionableUnit.DeviceId"/> and treat a repeat as an update, not a new
    /// candidate.
    /// </remarks>
    IAsyncEnumerable<ProvisionableUnit> ScanForProvisionableUnitsAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delivers <paramref name="payload"/> to <paramref name="unit"/> and returns the unit's
    /// provisioning result. The unit is one previously yielded by
    /// <see cref="ScanForProvisionableUnitsAsync"/>, so the transport can target it without the
    /// caller handling transport addressing.
    /// </summary>
    Task<ProvisioningResult> ProvisionUnitAsync(
        ProvisionableUnit unit,
        ProvisioningPayload payload,
        CancellationToken cancellationToken = default);
}
