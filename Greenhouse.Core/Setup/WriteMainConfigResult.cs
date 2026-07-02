using Greenhouse.Core.Configuration;

namespace Greenhouse.Core.Setup;

/// <summary>
/// Outcome of <see cref="WriteMainConfig"/>. A closed hierarchy: exactly one of
/// <see cref="Success"/>, <see cref="ValidationError"/>, or <see cref="AlreadyExists"/>.
/// </summary>
public abstract record WriteMainConfigResult
{
    private WriteMainConfigResult()
    {
    }

    public sealed record Success(MainConfig Config) : WriteMainConfigResult;

    public sealed record ValidationError(string Field, string Message) : WriteMainConfigResult;

    public sealed record AlreadyExists : WriteMainConfigResult;
}
