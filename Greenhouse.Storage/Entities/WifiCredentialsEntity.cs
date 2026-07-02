namespace Greenhouse.Storage.Entities;

/// <summary>
/// EF Core entity mapped to the single-row <c>WifiCredentials</c> table.
/// Infrastructure-only: must not be referenced outside <c>Greenhouse.Storage</c>.
/// Password is stored as plaintext in Phase 1; encryption at rest is deferred.
/// </summary>
internal sealed class WifiCredentialsEntity
{
    public int Id { get; set; }

    public string NetworkName { get; set; } = null!;

    public string Password { get; set; } = null!;

    public DateTime SavedAt { get; set; }
}
