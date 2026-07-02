using Greenhouse.Api.Contracts;
using Greenhouse.Api.Controllers;
using Greenhouse.Core.Configuration;
using Greenhouse.Core.Setup;
using Microsoft.AspNetCore.Mvc;

namespace Greenhouse.Api.Tests;

public class SetupControllerTests
{
    private static MainConfig SomeConfig() =>
        new("North", "Block A", null, DateTime.UnixEpoch, DateTime.UnixEpoch);

    private static async Task<SetupStatusResponse> GetStatus(bool configExists, bool online)
    {
        var repository = new FakeMainConfigRepository { Current = configExists ? SomeConfig() : null };
        var connector = new FakeNetworkConnector { Online = online };
        var controller = new SetupController(new ReadSetupStatus(repository, connector));

        var result = await controller.GetStatus(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<SetupStatusResponse>(ok.Value);
    }

    [Fact]
    public async Task No_config_offline_returns_network_connection()
    {
        var body = await GetStatus(configExists: false, online: false);
        Assert.Equal(SetupSteps.NetworkConnection, body.RequiredStep);
        Assert.False(body.SetupComplete);
    }

    [Fact]
    public async Task No_config_online_returns_main_config()
    {
        var body = await GetStatus(configExists: false, online: true);
        Assert.Equal(SetupSteps.MainConfig, body.RequiredStep);
    }

    [Fact]
    public async Task Config_offline_returns_network_recovery()
    {
        var body = await GetStatus(configExists: true, online: false);
        Assert.Equal(SetupSteps.NetworkRecovery, body.RequiredStep);
    }

    [Fact]
    public async Task Config_online_is_complete()
    {
        var body = await GetStatus(configExists: true, online: true);
        Assert.True(body.SetupComplete);
        Assert.Null(body.RequiredStep);
    }
}
