using Greenhouse.Core.Networking;
using Greenhouse.Core.Setup;

namespace Greenhouse.Core.Tests.Setup;

public class ConnectToNetworkTests
{
    [Fact]
    public async Task Persists_credentials_on_Connected()
    {
        var connector = new FakeNetworkConnector { ConnectResult = new ConnectResult.Connected() };
        var credentials = new FakeWifiCredentialsRepository();

        var result = await new ConnectToNetwork(connector, credentials).ExecuteAsync("MyNetwork", "secret");

        Assert.IsType<ConnectResult.Connected>(result);
        Assert.Equal(1, credentials.SaveCount);
        Assert.Equal("MyNetwork", credentials.Current!.NetworkName);
        Assert.Equal("secret", credentials.Current.Password);
    }

    [Fact]
    public async Task Does_not_persist_on_Failed()
    {
        var connector = new FakeNetworkConnector { ConnectResult = new ConnectResult.Failed("auth") };
        var credentials = new FakeWifiCredentialsRepository();

        var result = await new ConnectToNetwork(connector, credentials).ExecuteAsync("MyNetwork", "secret");

        Assert.IsType<ConnectResult.Failed>(result);
        Assert.Equal(0, credentials.SaveCount);
    }

    [Fact]
    public async Task Does_not_persist_on_TimedOut()
    {
        var connector = new FakeNetworkConnector { ConnectResult = new ConnectResult.TimedOut() };
        var credentials = new FakeWifiCredentialsRepository();

        await new ConnectToNetwork(connector, credentials).ExecuteAsync("MyNetwork", "secret");

        Assert.Equal(0, credentials.SaveCount);
    }

    [Fact]
    public async Task Trims_network_name_before_connect_and_persist()
    {
        var connector = new FakeNetworkConnector { ConnectResult = new ConnectResult.Connected() };
        var credentials = new FakeWifiCredentialsRepository();

        await new ConnectToNetwork(connector, credentials).ExecuteAsync("  MyNetwork  ", "secret");

        Assert.Equal("MyNetwork", connector.LastNetworkName);
        Assert.Equal("MyNetwork", credentials.Current!.NetworkName);
    }

    [Fact]
    public async Task Persists_empty_password_for_open_network()
    {
        var connector = new FakeNetworkConnector { ConnectResult = new ConnectResult.Connected() };
        var credentials = new FakeWifiCredentialsRepository();

        await new ConnectToNetwork(connector, credentials).ExecuteAsync("OpenNetwork", password: null);

        Assert.Equal(string.Empty, credentials.Current!.Password);
    }
}
