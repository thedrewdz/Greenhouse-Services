using Greenhouse.Core.Configuration;
using Greenhouse.Core.Setup;

namespace Greenhouse.Core.Tests.Setup;

public class WifiCredentialsUseCaseTests
{
    [Fact]
    public async Task GetWifiCredentials_returns_stored_credentials()
    {
        var credentials = new FakeWifiCredentialsRepository { Current = new WifiCredentials("MyNetwork", "secret") };

        var result = await new GetWifiCredentials(credentials).ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("MyNetwork", result!.NetworkName);
        Assert.Equal("secret", result.Password);
    }

    [Fact]
    public async Task GetWifiCredentials_returns_null_when_none_stored()
    {
        var result = await new GetWifiCredentials(new FakeWifiCredentialsRepository()).ExecuteAsync();

        Assert.Null(result);
    }

    [Fact]
    public async Task ReadMainConfig_returns_stored_config()
    {
        var repository = new FakeMainConfigRepository
        {
            Current = new MainConfig("North", "Block A", null, DateTime.UnixEpoch, DateTime.UnixEpoch),
        };

        var result = await new ReadMainConfig(repository).ExecuteAsync();

        Assert.NotNull(result);
        Assert.Equal("North", result!.GreenhouseName);
    }
}
