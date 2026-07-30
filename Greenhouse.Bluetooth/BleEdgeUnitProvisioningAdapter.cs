using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Greenhouse.Core.Onboarding;
using Microsoft.Extensions.Logging;

namespace Greenhouse.Bluetooth;

/// <summary>
/// Bridges the application port <see cref="IEdgeUnitProvisioningTransport"/> to the low-level
/// <see cref="IBleTransport"/>. This class alone owns the canonical GATT UUIDs, the
/// advertising-name prefix, and the JSON (de)serialisation of the provisioning payload and status
/// response. None of those transport details leak above this layer.
/// </summary>
internal sealed class BleEdgeUnitProvisioningAdapter : IEdgeUnitProvisioningTransport
{
    // Canonical onboarding GATT identifiers (specs/edge-unit-onboarding/spec.md). Private to this
    // class — they must never appear in a contract or be visible outside Greenhouse.Bluetooth.
    private const string AdvertisingNamePrefix = "GH-Edge-";
    private static readonly Guid OnboardingServiceUuid = new("00014452-414f-424e-4f2d-454744454847");
    private static readonly Guid ProvisioningPayloadCharacteristicUuid = new("00024452-414f-424e-4f2d-454744454847");
    private static readonly Guid ProvisioningStatusCharacteristicUuid = new("00034452-414f-424e-4f2d-454744454847");

    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IBleTransport _transport;
    private readonly ILogger<BleEdgeUnitProvisioningAdapter> _logger;

    public BleEdgeUnitProvisioningAdapter(
        IBleTransport transport,
        ILogger<BleEdgeUnitProvisioningAdapter> logger)
    {
        _transport = transport;
        _logger = logger;
    }

    public async IAsyncEnumerable<ProvisionableUnit> ScanForProvisionableUnitsAsync(
        TimeSpan timeout,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Scanning begins only here — never at process startup. The transport bounds the scan
        // window; the caller's token still cancels it.
        var filter = new BleScanFilter(NamePrefix: AdvertisingNamePrefix, ServiceUuid: OnboardingServiceUuid);

        await foreach (var device in _transport.ScanAsync(filter, timeout, cancellationToken))
        {
            yield return new ProvisionableUnit(
                DeriveDeviceId(device),
                device.DeviceId,
                device.Name,
                device.Rssi);
        }
    }

    /// <summary>
    /// Extracts the Edge Unit hardware identity from its advertised name. The
    /// <c>GH-Edge-{device_id}</c> convention is owned here, so nothing above this adapter has to
    /// know it. Falls back to the transport address if a unit advertises an unexpected name.
    /// </summary>
    private static string DeriveDeviceId(BleDeviceInfo device) =>
        device.Name.StartsWith(AdvertisingNamePrefix, StringComparison.OrdinalIgnoreCase)
            ? device.Name[AdvertisingNamePrefix.Length..]
            : device.DeviceId;

    public async Task<ProvisioningResult> ProvisionUnitAsync(
        ProvisionableUnit unit,
        ProvisioningPayload payload,
        CancellationToken cancellationToken = default)
    {
        // Never log the payload or its WiFi password — only the non-sensitive device identity.
        _logger.LogInformation("Provisioning Edge Unit '{DeviceId}'.", unit.DeviceId);

        var json = SerializePayload(payload);
        var payloadBytes = Encoding.UTF8.GetBytes(json);
        var address = unit.TransportAddress;

        await _transport.ConnectAsync(address, cancellationToken);
        try
        {
            await _transport.WriteCharacteristicAsync(
                address,
                OnboardingServiceUuid,
                ProvisioningPayloadCharacteristicUuid,
                payloadBytes,
                cancellationToken);

            var statusBytes = await _transport.ReadCharacteristicAsync(
                address,
                OnboardingServiceUuid,
                ProvisioningStatusCharacteristicUuid,
                cancellationToken);

            return ParseStatus(statusBytes);
        }
        finally
        {
            // Always tear down the connection, even when writing or reading throws.
            await SafeDisconnectAsync(address);
        }
    }

    private static string SerializePayload(ProvisioningPayload payload)
    {
        var dto = new ProvisioningPayloadDto(
            SchemaVersion,
            payload.DeviceId,
            payload.WifiSsid,
            payload.WifiPassword,
            payload.MqttBrokerUri,
            payload.HeartbeatIntervalMs);

        return JsonSerializer.Serialize(dto, SerializerOptions);
    }

    private ProvisioningResult ParseStatus(byte[] statusBytes)
    {
        if (statusBytes.Length == 0)
        {
            return new ProvisioningResult.Failed(2099, "Empty provisioning status response.");
        }

        ProvisioningStatusDto? status;
        try
        {
            status = JsonSerializer.Deserialize<ProvisioningStatusDto>(
                Encoding.UTF8.GetString(statusBytes),
                SerializerOptions);
        }
        catch (JsonException)
        {
            return new ProvisioningResult.Failed(2099, "Malformed provisioning status response.");
        }

        if (status is null)
        {
            return new ProvisioningResult.Failed(2099, "Malformed provisioning status response.");
        }

        if (string.Equals(status.Result, "success", StringComparison.OrdinalIgnoreCase))
        {
            return new ProvisioningResult.Success();
        }

        return new ProvisioningResult.Failed(
            status.ErrorCode,
            string.IsNullOrEmpty(status.ErrorMessage) ? "Provisioning failed." : status.ErrorMessage);
    }

    private async Task SafeDisconnectAsync(string deviceId)
    {
        try
        {
            await _transport.DisconnectAsync(deviceId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanly disconnect Edge Unit '{DeviceId}'.", deviceId);
        }
    }

    private sealed record ProvisioningPayloadDto(
        [property: JsonPropertyName("schema_version")] int SchemaVersion,
        [property: JsonPropertyName("device_id")] string DeviceId,
        [property: JsonPropertyName("wifi_ssid")] string WifiSsid,
        [property: JsonPropertyName("wifi_password")] string WifiPassword,
        [property: JsonPropertyName("mqtt_broker_uri")] string MqttBrokerUri,
        [property: JsonPropertyName("heartbeat_interval_ms")] int? HeartbeatIntervalMs);

    private sealed record ProvisioningStatusDto(
        [property: JsonPropertyName("result")] string? Result,
        [property: JsonPropertyName("error_code")] int ErrorCode,
        [property: JsonPropertyName("error_message")] string? ErrorMessage);
}
