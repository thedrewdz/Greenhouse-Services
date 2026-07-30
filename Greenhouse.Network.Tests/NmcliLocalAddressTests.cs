using Microsoft.Extensions.Logging.Abstractions;

namespace Greenhouse.Network.Tests;

/// <summary>
/// Covers <c>GetLocalAddressAsync</c>, the address Edge Unit onboarding derives
/// <c>mqtt_broker_uri</c> from. An unusable address here means an Edge Unit that can never reach
/// the broker, so the negative paths matter as much as the happy one.
/// </summary>
public class NmcliLocalAddressTests
{
    private static NmcliNetworkAdapter Create(string standardOutput) =>
        new(
            new FakeNmcliCommandRunner(new NmcliResult(0, standardOutput, string.Empty)),
            NullLogger<NmcliNetworkAdapter>.Instance);

    [Fact]
    public async Task Returns_the_ipv4_address_without_its_cidr_suffix()
    {
        var adapter = Create("IP4.ADDRESS[1]:192.168.1.50/24\n");

        Assert.Equal("192.168.1.50", await adapter.GetLocalAddressAsync());
    }

    [Fact]
    public async Task Returns_null_when_no_device_reports_an_address()
    {
        var adapter = Create(string.Empty);

        Assert.Null(await adapter.GetLocalAddressAsync());
    }

    [Fact]
    public async Task Skips_loopback_and_link_local_addresses()
    {
        // An offline Pi still reports loopback, and a failed DHCP lease leaves a link-local
        // address; neither is reachable from an Edge Unit.
        var adapter = Create(
            "IP4.ADDRESS[1]:127.0.0.1/8\n" +
            "IP4.ADDRESS[1]:169.254.10.5/16\n" +
            "IP4.ADDRESS[1]:10.0.0.7/24\n");

        Assert.Equal("10.0.0.7", await adapter.GetLocalAddressAsync());
    }

    [Fact]
    public async Task Returns_null_when_only_unusable_addresses_are_reported()
    {
        var adapter = Create("IP4.ADDRESS[1]:127.0.0.1/8\nIP4.ADDRESS[1]:169.254.10.5/16\n");

        Assert.Null(await adapter.GetLocalAddressAsync());
    }

    [Fact]
    public async Task Ignores_malformed_lines()
    {
        var adapter = Create("garbage\nIP4.ADDRESS[1]:not-an-address\nIP4.ADDRESS[1]:192.168.4.94/24\n");

        Assert.Equal("192.168.4.94", await adapter.GetLocalAddressAsync());
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("192. 168.1.5")]
    [InlineData("192.168.1.+5")]
    [InlineData("10.1")]
    [InlineData("::1")]
    [InlineData("192.168.1.256")]
    public async Task Rejects_addresses_an_Edge_Unit_could_never_reach_the_broker_on(string address)
    {
        var adapter = Create($"IP4.ADDRESS[1]:{address}/24\n");

        Assert.Null(await adapter.GetLocalAddressAsync());
    }

    [Fact]
    public async Task Queries_nmcli_for_device_ipv4_addresses()
    {
        var runner = new FakeNmcliCommandRunner(new NmcliResult(0, string.Empty, string.Empty));

        await new NmcliNetworkAdapter(runner, NullLogger<NmcliNetworkAdapter>.Instance).GetLocalAddressAsync();

        Assert.Equal(
            new[] { "-t", "-f", "IP4.ADDRESS", "device", "show" },
            Assert.Single(runner.Invocations));
    }
}
