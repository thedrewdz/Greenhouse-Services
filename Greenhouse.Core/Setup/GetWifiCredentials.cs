using Greenhouse.Core.Configuration;

namespace Greenhouse.Core.Setup;

/// <summary>
/// Retrieves the stored WiFi credentials for the Edge Unit onboarding flow. Internal service
/// use only — never exposed through the API.
/// </summary>
public sealed class GetWifiCredentials
{
    private readonly IWifiCredentialsRepository _credentials;

    public GetWifiCredentials(IWifiCredentialsRepository credentials)
    {
        _credentials = credentials;
    }

    public Task<WifiCredentials?> ExecuteAsync(CancellationToken cancellationToken = default) =>
        _credentials.GetAsync();
}
