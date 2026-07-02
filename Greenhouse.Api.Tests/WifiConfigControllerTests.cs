using Greenhouse.Api.Contracts;
using Greenhouse.Api.Controllers;
using Greenhouse.Core.Networking;
using Greenhouse.Core.Setup;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Greenhouse.Api.Tests;

public class WifiConfigControllerTests
{
    private static WifiConfigController Create(FakeNetworkConnector connector)
    {
        var connectToNetwork = new ConnectToNetwork(connector, new FakeWifiCredentialsRepository());
        return new WifiConfigController(connector, connectToNetwork);
    }

    [Fact]
    public async Task Get_returns_online_status_and_network_name()
    {
        var connector = new FakeNetworkConnector { Online = true, CurrentNetworkName = "MyNetwork" };

        var result = await Create(connector).Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<WifiStatusResponse>(ok.Value);
        Assert.True(body.IsOnline);
        Assert.Equal("MyNetwork", body.NetworkName);
    }

    [Fact]
    public async Task Get_returns_offline_status_with_null_name()
    {
        var connector = new FakeNetworkConnector { Online = false, CurrentNetworkName = null };

        var result = await Create(connector).Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<WifiStatusResponse>(ok.Value);
        Assert.False(body.IsOnline);
        Assert.Null(body.NetworkName);
    }

    [Fact]
    public async Task Get_returns_503_when_connector_unavailable()
    {
        var result = await Create(new FakeNetworkConnector { ThrowUnavailable = true }).Get(CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);
    }

    [Fact]
    public async Task Post_returns_200_connected_on_success()
    {
        var connector = new FakeNetworkConnector { ConnectResult = new ConnectResult.Connected() };

        var result = await Create(connector).Post(new WifiConfigRequest("MyNetwork", "secret"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<WifiConnectResponse>(ok.Value);
        Assert.True(body.Connected);
        Assert.Null(body.ErrorMessage);
    }

    [Fact]
    public async Task Post_returns_422_on_auth_failure()
    {
        var connector = new FakeNetworkConnector { ConnectResult = new ConnectResult.Failed("Authentication failed") };

        var result = await Create(connector).Post(new WifiConfigRequest("MyNetwork", "wrong"), CancellationToken.None);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        var body = Assert.IsType<WifiConnectResponse>(unprocessable.Value);
        Assert.False(body.Connected);
        Assert.Equal("Authentication failed", body.ErrorMessage);
    }

    [Fact]
    public async Task Post_returns_504_on_timeout()
    {
        var connector = new FakeNetworkConnector { ConnectResult = new ConnectResult.TimedOut() };

        var result = await Create(connector).Post(new WifiConfigRequest("MyNetwork", "secret"), CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status504GatewayTimeout, status.StatusCode);
    }

    [Fact]
    public async Task Post_returns_503_when_connector_unavailable()
    {
        var connector = new FakeNetworkConnector { ThrowUnavailable = true };

        var result = await Create(connector).Post(new WifiConfigRequest("MyNetwork", "secret"), CancellationToken.None);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, status.StatusCode);
    }

    [Fact]
    public async Task Post_returns_422_envelope_when_network_name_missing()
    {
        var result = await Create(new FakeNetworkConnector())
            .Post(new WifiConfigRequest("   ", "secret"), CancellationToken.None);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        Assert.IsType<ValidationErrorEnvelope>(unprocessable.Value);
    }
}
