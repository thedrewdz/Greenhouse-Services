namespace Greenhouse.Storage.Entities;

/// <summary>
/// EF Core entity mapped to the single-row <c>MainConfigs</c> table.
/// Infrastructure-only: must not be referenced outside <c>Greenhouse.Storage</c>.
/// </summary>
internal sealed class MainConfigEntity
{
    public int Id { get; set; }

    public string GreenhouseName { get; set; } = null!;

    public string Location { get; set; } = null!;

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
