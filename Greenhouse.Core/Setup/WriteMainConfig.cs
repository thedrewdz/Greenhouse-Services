using Greenhouse.Core.Configuration;

namespace Greenhouse.Core.Setup;

/// <summary>Validates and persists the first (and only) MainConfig.</summary>
public sealed class WriteMainConfig
{
    private readonly IMainConfigRepository _repository;
    private readonly TimeProvider _timeProvider;

    public WriteMainConfig(IMainConfigRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<WriteMainConfigResult> ExecuteAsync(
        string greenhouseName,
        string location,
        string? description,
        CancellationToken cancellationToken = default)
    {
        var errors = MainConfigValidation.Validate(greenhouseName, location, description);
        if (errors.Count > 0)
        {
            return new WriteMainConfigResult.ValidationError(errors[0].Field, errors[0].Message);
        }

        if (await _repository.GetAsync() is not null)
        {
            return new WriteMainConfigResult.AlreadyExists();
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var config = new MainConfig(greenhouseName, location, description, now, now);
        await _repository.CreateAsync(config);
        return new WriteMainConfigResult.Success(config);
    }
}
