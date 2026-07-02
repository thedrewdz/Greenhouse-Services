using Greenhouse.Core.Networking;
using Microsoft.Extensions.Logging.Abstractions;

namespace Greenhouse.Network.Tests;

public class NmcliNetworkAdapterTests
{
    private static NmcliNetworkAdapter CreateAdapter(
        INmcliCommandRunner runner,
        CapturingLogger<NmcliNetworkAdapter>? logger = null,
        TimeSpan? timeout = null) =>
        new(runner, logger ?? new CapturingLogger<NmcliNetworkAdapter>(), timeout);

    [Theory]
    [InlineData("full\n", true)]
    [InlineData("none\n", false)]
    [InlineData("limited\n", false)]
    public async Task IsOnlineAsync_maps_connectivity(string stdout, bool expected)
    {
        var adapter = CreateAdapter(new FakeNmcliCommandRunner(new NmcliResult(0, stdout, string.Empty)));

        Assert.Equal(expected, await adapter.IsOnlineAsync());
    }

    [Fact]
    public async Task GetCurrentNetworkNameAsync_returns_active_ssid()
    {
        var runner = new FakeNmcliCommandRunner(new NmcliResult(0, "no:OtherNet\nyes:MyNetwork\n", string.Empty));
        var adapter = CreateAdapter(runner);

        Assert.Equal("MyNetwork", await adapter.GetCurrentNetworkNameAsync());
    }

    [Fact]
    public async Task GetCurrentNetworkNameAsync_returns_null_when_none_active()
    {
        var runner = new FakeNmcliCommandRunner(new NmcliResult(0, "no:OtherNet\nno:Another\n", string.Empty));
        var adapter = CreateAdapter(runner);

        Assert.Null(await adapter.GetCurrentNetworkNameAsync());
    }

    [Fact]
    public async Task ConnectAsync_returns_Connected_and_passes_password_argument()
    {
        var runner = new FakeNmcliCommandRunner(new NmcliResult(0, "Device successfully activated", string.Empty));
        var adapter = CreateAdapter(runner);

        var result = await adapter.ConnectAsync("MyNetwork", "secret");

        Assert.IsType<ConnectResult.Connected>(result);
        var args = Assert.Single(runner.Invocations);
        Assert.Contains("password", args);
        Assert.Contains("secret", args);
    }

    [Fact]
    public async Task ConnectAsync_omits_password_argument_for_open_network()
    {
        var runner = new FakeNmcliCommandRunner(new NmcliResult(0, string.Empty, string.Empty));
        var adapter = CreateAdapter(runner);

        await adapter.ConnectAsync("OpenNetwork", password: null);

        var args = Assert.Single(runner.Invocations);
        Assert.DoesNotContain("password", args);
    }

    [Fact]
    public async Task ConnectAsync_returns_Failed_with_message_on_nonzero_exit()
    {
        var runner = new FakeNmcliCommandRunner(
            new NmcliResult(4, string.Empty, "Error: Secrets were required, but not provided."));
        var adapter = CreateAdapter(runner);

        var result = await adapter.ConnectAsync("MyNetwork", "wrong-pass");

        var failed = Assert.IsType<ConnectResult.Failed>(result);
        Assert.Contains("Secrets were required", failed.ErrorMessage);
    }

    [Fact]
    public async Task ConnectAsync_returns_TimedOut_when_timeout_elapses()
    {
        // Runner blocks until its token is cancelled; the adapter's short timeout fires it.
        var runner = new FakeNmcliCommandRunner(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return new NmcliResult(0, string.Empty, string.Empty);
        });
        var adapter = CreateAdapter(runner, timeout: TimeSpan.FromMilliseconds(20));

        var result = await adapter.ConnectAsync("MyNetwork", "secret");

        Assert.IsType<ConnectResult.TimedOut>(result);
    }

    [Fact]
    public async Task ConnectAsync_never_logs_the_password()
    {
        const string password = "sup3r-s3cret";
        var logger = new CapturingLogger<NmcliNetworkAdapter>();
        // Failure path echoes the password in stderr to prove redaction covers logs and the result.
        var runner = new FakeNmcliCommandRunner(
            new NmcliResult(4, string.Empty, $"Error: password {password} rejected"));
        var adapter = CreateAdapter(runner, logger);

        var result = await adapter.ConnectAsync("MyNetwork", password);

        Assert.All(logger.Messages, message => Assert.DoesNotContain(password, message));
        var failed = Assert.IsType<ConnectResult.Failed>(result);
        Assert.DoesNotContain(password, failed.ErrorMessage);
    }

    [Fact]
    public async Task ConnectAsync_propagates_caller_cancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var runner = new FakeNmcliCommandRunner((_, token) => Task.FromCanceled<NmcliResult>(token));
        var adapter = CreateAdapter(runner);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => adapter.ConnectAsync("MyNetwork", "secret", cts.Token));
    }
}
