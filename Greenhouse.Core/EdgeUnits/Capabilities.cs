namespace Greenhouse.Core.EdgeUnits;

/// <summary>
/// The canonical capability vocabulary the Main Unit accepts in a slot mapping. Mapping
/// validation rejects any capability outside this set
/// (<c>specs/edge-unit-configuration/spec.md</c>, "Main Unit Input Validation").
/// </summary>
/// <remarks>
/// Sourced from the capability examples in <c>device-model.md</c> plus the slot capabilities
/// used in the canonical heartbeat and configuration payloads. Time-of-day and season are
/// deliberately excluded: they are automation inputs, not peripheral slot capabilities.
/// </remarks>
public static class Capabilities
{
    private static readonly HashSet<string> Canonical = new(StringComparer.Ordinal)
    {
        "moisture",
        "temperature",
        "humidity",
        "light",
        "co2",
        "ph",
        "ec-tds",
        "pump",
        "valve",
        "camera",
    };

    /// <summary>The canonical capability names, ordered for stable presentation.</summary>
    public static IReadOnlyCollection<string> All { get; } = Canonical.OrderBy(c => c, StringComparer.Ordinal).ToArray();

    public static bool IsCanonical(string? capability) =>
        !string.IsNullOrWhiteSpace(capability) && Canonical.Contains(capability);
}
