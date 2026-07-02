namespace Greenhouse.Core.Setup;

/// <summary>
/// Single source of the MainConfig field rules, shared by the API boundary (which builds the
/// multi-field error envelope) and the <see cref="WriteMainConfig"/> use case (which uses it
/// as a backstop).
/// </summary>
public static class MainConfigValidation
{
    public const int GreenhouseNameMaxLength = 50;
    public const int LocationMaxLength = 50;
    public const int DescriptionMaxLength = 100;

    /// <summary>Returns one entry per broken rule; empty when the input is valid.</summary>
    public static IReadOnlyList<(string Field, string Message)> Validate(
        string? greenhouseName,
        string? location,
        string? description)
    {
        var errors = new List<(string Field, string Message)>();

        if (string.IsNullOrWhiteSpace(greenhouseName))
        {
            errors.Add(("greenhouseName", "Greenhouse name is required."));
        }
        else if (greenhouseName.Length > GreenhouseNameMaxLength)
        {
            errors.Add(("greenhouseName", $"Greenhouse name must not exceed {GreenhouseNameMaxLength} characters."));
        }

        if (string.IsNullOrWhiteSpace(location))
        {
            errors.Add(("location", "Location is required."));
        }
        else if (location.Length > LocationMaxLength)
        {
            errors.Add(("location", $"Location must not exceed {LocationMaxLength} characters."));
        }

        if (description is not null && description.Length > DescriptionMaxLength)
        {
            errors.Add(("description", $"Description must not exceed {DescriptionMaxLength} characters."));
        }

        return errors;
    }
}
