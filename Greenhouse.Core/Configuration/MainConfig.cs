namespace Greenhouse.Core.Configuration;

/// <summary>
/// Application-layer model for the greenhouse identity persisted by the Main Unit.
/// At most one exists per Main Unit. Technology-neutral: carries no persistence concerns.
/// </summary>
public sealed record MainConfig(
    string GreenhouseName,
    string Location,
    string? Description,
    DateTime CreatedAt,
    DateTime UpdatedAt);
