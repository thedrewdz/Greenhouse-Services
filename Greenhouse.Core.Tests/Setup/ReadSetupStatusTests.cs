using Greenhouse.Core.Configuration;
using Greenhouse.Core.Setup;

namespace Greenhouse.Core.Tests.Setup;

public class ReadSetupStatusTests
{
    private static MainConfig SomeConfig() =>
        new("North", "Block A", null, DateTime.UnixEpoch, DateTime.UnixEpoch);

    private static ReadSetupStatus Create(bool configExists, bool online)
    {
        var repository = new FakeMainConfigRepository { Current = configExists ? SomeConfig() : null };
        var connector = new FakeNetworkConnector { Online = online };
        return new ReadSetupStatus(repository, connector);
    }

    [Fact]
    public async Task No_config_offline_requires_network_connection()
    {
        var status = await Create(configExists: false, online: false).ExecuteAsync();

        Assert.False(status.SetupComplete);
        Assert.False(status.IsOnline);
        Assert.Equal(SetupSteps.NetworkConnection, status.RequiredStep);
    }

    [Fact]
    public async Task No_config_online_requires_main_config()
    {
        var status = await Create(configExists: false, online: true).ExecuteAsync();

        Assert.False(status.SetupComplete);
        Assert.True(status.IsOnline);
        Assert.Equal(SetupSteps.MainConfig, status.RequiredStep);
    }

    [Fact]
    public async Task Config_offline_requires_network_recovery()
    {
        var status = await Create(configExists: true, online: false).ExecuteAsync();

        Assert.False(status.SetupComplete);
        Assert.Equal(SetupSteps.NetworkRecovery, status.RequiredStep);
    }

    [Fact]
    public async Task Config_online_is_complete()
    {
        var status = await Create(configExists: true, online: true).ExecuteAsync();

        Assert.True(status.SetupComplete);
        Assert.True(status.IsOnline);
        Assert.Null(status.RequiredStep);
    }
}
