namespace Greenhouse.Core.Configuration;

/// <summary>
/// Persistence contract for the single-row MainConfig store. Implemented in
/// <c>Greenhouse.Storage</c>; application code depends only on this interface.
/// </summary>
public interface IMainConfigRepository
{
    /// <summary>Returns the current MainConfig, or <c>null</c> when none exists.</summary>
    Task<MainConfig?> GetAsync();

    /// <summary>Persists the first (and only) MainConfig.</summary>
    Task CreateAsync(MainConfig config);

    /// <summary>Updates the existing MainConfig in place.</summary>
    Task UpdateAsync(MainConfig config);

    /// <summary>Removes the MainConfig if present.</summary>
    Task DeleteAsync();
}
