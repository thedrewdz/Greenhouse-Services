using System.Reflection;
using System.Text;
using System.Text.Json;
using Greenhouse.Core.Onboarding;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Greenhouse.Bluetooth.Tests;

public class BleEdgeUnitProvisioningAdapterTests
{
    private static BleEdgeUnitProvisioningAdapter Create(
        FakeBleTransport transport,
        ILogger<BleEdgeUnitProvisioningAdapter>? logger = null) =>
        new(transport, logger ?? NullLogger<BleEdgeUnitProvisioningAdapter>.Instance);

    private static ProvisioningPayload SamplePayload(string password = "secret") =>
        new("1ADD5912AF61", "MyWifi", password, "mqtt://192.168.1.50", 30000);

    [Fact]
    public async Task Scan_maps_ble_devices_to_provisionable_units()
    {
        var transport = new FakeBleTransport
        {
            ScanResult = new[]
            {
                new BleDeviceInfo("AA:BB:CC:DD:EE:FF", "GH-Edge-1ADD5912AF61", -40),
            },
        };

        var units = await Create(transport).ScanForProvisionableUnitsAsync(TimeSpan.FromSeconds(5));

        var unit = Assert.Single(units);
        Assert.Equal("AA:BB:CC:DD:EE:FF", unit.DeviceId);
        Assert.Equal("GH-Edge-1ADD5912AF61", unit.AdvertisedName);
        Assert.Equal("GH-Edge-", transport.LastScanFilter!.NamePrefix);
    }

    [Fact]
    public async Task Provision_writes_canonical_snake_case_json()
    {
        var transport = new FakeBleTransport();

        await Create(transport).ProvisionUnitAsync("AA:BB:CC:DD:EE:FF", SamplePayload());

        Assert.NotNull(transport.WrittenPayload);
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(transport.WrittenPayload!));
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("schema_version").GetInt32());
        Assert.Equal("1ADD5912AF61", root.GetProperty("device_id").GetString());
        Assert.Equal("MyWifi", root.GetProperty("wifi_ssid").GetString());
        Assert.Equal("secret", root.GetProperty("wifi_password").GetString());
        Assert.Equal("mqtt://192.168.1.50", root.GetProperty("mqtt_broker_uri").GetString());
        Assert.Equal(30000, root.GetProperty("heartbeat_interval_ms").GetInt32());
    }

    [Fact]
    public async Task Provision_omits_heartbeat_when_not_supplied()
    {
        var transport = new FakeBleTransport();
        var payload = new ProvisioningPayload("1ADD5912AF61", "MyWifi", "secret", "mqtt://192.168.1.50");

        await Create(transport).ProvisionUnitAsync("dev", payload);

        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(transport.WrittenPayload!));
        Assert.False(doc.RootElement.TryGetProperty("heartbeat_interval_ms", out _));
    }

    [Fact]
    public async Task Provision_returns_Success_on_success_status()
    {
        var transport = new FakeBleTransport
        {
            StatusResponse = Encoding.UTF8.GetBytes("{\"result\":\"success\",\"error_code\":0,\"error_message\":\"\"}"),
        };

        var result = await Create(transport).ProvisionUnitAsync("dev", SamplePayload());

        Assert.IsType<ProvisioningResult.Success>(result);
    }

    [Fact]
    public async Task Provision_returns_Failed_with_error_code_on_error_status()
    {
        var transport = new FakeBleTransport
        {
            StatusResponse = Encoding.UTF8.GetBytes(
                "{\"result\":\"error\",\"error_code\":2004,\"error_message\":\"mqtt_broker_uri_invalid\"}"),
        };

        var result = await Create(transport).ProvisionUnitAsync("dev", SamplePayload());

        var failed = Assert.IsType<ProvisioningResult.Failed>(result);
        Assert.Equal(2004, failed.ErrorCode);
        Assert.Equal("mqtt_broker_uri_invalid", failed.ErrorMessage);
    }

    [Fact]
    public async Task Provision_disconnects_even_when_write_throws()
    {
        var transport = new FakeBleTransport { ThrowOnWrite = true };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Create(transport).ProvisionUnitAsync("dev", SamplePayload()));

        Assert.Equal(1, transport.DisconnectCount);
    }

    [Fact]
    public async Task Provision_never_logs_the_wifi_password()
    {
        const string password = "sup3r-s3cret-pw";
        var transport = new FakeBleTransport();
        var logger = new CapturingLogger<BleEdgeUnitProvisioningAdapter>();

        await new BleEdgeUnitProvisioningAdapter(transport, logger)
            .ProvisionUnitAsync("dev", SamplePayload(password));

        Assert.All(logger.Messages, message => Assert.DoesNotContain(password, message));
    }

    [Fact]
    public void No_gatt_uuid_is_visible_outside_the_adapter()
    {
        // All GATT UUIDs must be private constants — no non-private Guid members on the type.
        var guidMembers = typeof(BleEdgeUnitProvisioningAdapter)
            .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)
            .Where(m =>
                (m is FieldInfo field && !field.IsPrivate && field.FieldType == typeof(Guid))
                || (m is PropertyInfo prop && prop.PropertyType == typeof(Guid)));

        Assert.Empty(guidMembers);
    }
}
