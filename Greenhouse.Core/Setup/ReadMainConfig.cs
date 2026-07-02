using Greenhouse.Core.Configuration;

namespace Greenhouse.Core.Setup;

/// <summary>Retrieves the current MainConfig, or <c>null</c> when setup has not run.</summary>
public sealed class ReadMainConfig
{
    private readonly IMainConfigRepository _repository;

    public ReadMainConfig(IMainConfigRepository repository)
    {
        _repository = repository;
    }

    public Task<MainConfig?> ExecuteAsync(CancellationToken cancellationToken = default) =>
        _repository.GetAsync();
}
