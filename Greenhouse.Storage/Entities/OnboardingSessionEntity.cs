namespace Greenhouse.Storage.Entities;

/// <summary>
/// EF Core entity mapped to the single-row <c>OnboardingSessions</c> table. Phase 1 supports one
/// onboarding session at a time, so the table never holds more than one row.
/// Infrastructure-only: must not be referenced outside <c>Greenhouse.Storage</c>.
/// </summary>
internal sealed class OnboardingSessionEntity
{
    public int Id { get; set; }

    public string Status { get; set; } = null!;

    public string? SelectedDeviceId { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
