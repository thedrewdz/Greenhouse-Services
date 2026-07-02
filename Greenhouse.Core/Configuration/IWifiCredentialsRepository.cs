namespace Greenhouse.Core.Configuration;

/// <summary>
/// Persistence contract for the single-row WifiCredentials store. Implemented in
/// <c>Greenhouse.Storage</c>. Not exposed through the API — internal service use only.
/// </summary>
public interface IWifiCredentialsRepository
{
    /// <summary>Returns the stored credentials, or <c>null</c> when none exist.</summary>
    Task<WifiCredentials?> GetAsync();

    /// <summary>Upserts the credentials, replacing any existing row.</summary>
    Task SaveAsync(WifiCredentials credentials);

    /// <summary>Removes the stored credentials if present.</summary>
    Task DeleteAsync();
}
