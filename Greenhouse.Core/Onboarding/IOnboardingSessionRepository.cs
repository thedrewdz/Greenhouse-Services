namespace Greenhouse.Core.Onboarding;

/// <summary>
/// The persisted onboarding session. Only one session may be active at a time in Phase 1, so
/// this is a single-row store: <see cref="SaveAsync"/> upserts that row.
/// </summary>
/// <remarks>
/// Persisting the session keeps backend state authoritative across a daemon restart, so a UI
/// that reconnects mid-onboarding still sees the real status rather than an empty page.
/// Discovered candidates are deliberately not persisted — they are only meaningful while the
/// units are still advertising.
/// </remarks>
public interface IOnboardingSessionRepository
{
    /// <summary>Returns the stored session, or <c>null</c> when none has been started.</summary>
    Task<OnboardingSession?> GetCurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts or replaces the single session row.</summary>
    Task SaveAsync(OnboardingSession session, CancellationToken cancellationToken = default);

    /// <summary>Removes the session row, returning the store to its idle state.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>The persisted form of an onboarding session.</summary>
/// <param name="Status">One of <see cref="OnboardingStatuses"/>.</param>
/// <param name="SelectedDeviceId">Selected device, once the operator has chosen one.</param>
/// <param name="StartedAt">UTC timestamp the session started.</param>
/// <param name="UpdatedAt">UTC timestamp of the most recent state transition.</param>
public sealed record OnboardingSession(
    string Status,
    string? SelectedDeviceId,
    DateTime StartedAt,
    DateTime UpdatedAt);
