using Greenhouse.Core.Configuration;
using Greenhouse.Core.Networking;

namespace Greenhouse.Core.Setup;

/// <summary>
/// Assembles the first-run routing decision from config existence and connectivity state,
/// per the Main Unit Setup spec routing table.
/// </summary>
public sealed class ReadSetupStatus
{
    private readonly IMainConfigRepository _repository;
    private readonly INetworkConnector _connector;

    public ReadSetupStatus(IMainConfigRepository repository, INetworkConnector connector)
    {
        _repository = repository;
        _connector = connector;
    }

    public async Task<SetupStatus> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var configTask = _repository.GetAsync();
        var onlineTask = _connector.IsOnlineAsync(cancellationToken);
        await Task.WhenAll(configTask, onlineTask);

        var configExists = configTask.Result is not null;
        var isOnline = onlineTask.Result;

        var requiredStep = (configExists, isOnline) switch
        {
            (false, false) => SetupSteps.NetworkConnection,
            (false, true) => SetupSteps.MainConfig,
            (true, false) => SetupSteps.NetworkRecovery,
            (true, true) => null,
        };

        return new SetupStatus(
            SetupComplete: configExists && isOnline,
            IsOnline: isOnline,
            RequiredStep: requiredStep);
    }
}
