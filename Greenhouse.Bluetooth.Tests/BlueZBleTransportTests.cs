namespace Greenhouse.Bluetooth.Tests;

public class BlueZBleTransportTests
{
    private const string SampleScanOutput =
        "[NEW] Device AA:BB:CC:DD:EE:01 GH-Edge-1ADD5912AF61\n" +
        "[CHG] Device AA:BB:CC:DD:EE:01 RSSI: -40\n" +
        "[NEW] Device AA:BB:CC:DD:EE:02 GH-Edge-2BEEF0000001\n" +
        "[CHG] Device AA:BB:CC:DD:EE:02 RSSI: -70\n" +
        "[NEW] Device AA:BB:CC:DD:EE:03 SomeLaptop\n" +
        "[CHG] Device AA:BB:CC:DD:EE:03 RSSI: -50\n";

    [Fact]
    public void ParseScanOutput_returns_only_prefix_matches_ordered_by_rssi()
    {
        var filter = new BleScanFilter(NamePrefix: "GH-Edge-");

        var devices = BlueZBleTransport.ParseScanOutput(SampleScanOutput, filter);

        Assert.Equal(2, devices.Count);
        Assert.Equal("AA:BB:CC:DD:EE:01", devices[0].DeviceId);   // -40 sorts before -70
        Assert.Equal("GH-Edge-1ADD5912AF61", devices[0].Name);
        Assert.Equal(-40, devices[0].Rssi);
        Assert.Equal("AA:BB:CC:DD:EE:02", devices[1].DeviceId);
        Assert.DoesNotContain(devices, d => d.Name == "SomeLaptop");
    }

    [Fact]
    public void ParseScanOutput_without_filter_returns_all_named_devices()
    {
        var devices = BlueZBleTransport.ParseScanOutput(SampleScanOutput, new BleScanFilter());

        Assert.Equal(3, devices.Count);
    }

    [Fact]
    public void ParseReadValue_extracts_hex_bytes()
    {
        const string output = "Attribute value:\n  Value: 0x7b 0x22 0x6f 0x6b 0x22 0x7d\n";

        var bytes = BlueZBleTransport.ParseReadValue(output);

        Assert.Equal(new byte[] { 0x7b, 0x22, 0x6f, 0x6b, 0x22, 0x7d }, bytes);
    }
}
