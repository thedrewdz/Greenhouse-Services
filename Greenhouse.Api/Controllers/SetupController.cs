using Greenhouse.Api.Contracts;
using Greenhouse.Core.Setup;
using Microsoft.AspNetCore.Mvc;

namespace Greenhouse.Api.Controllers;

[ApiController]
[Route("api/setup")]
public sealed class SetupController : ControllerBase
{
    private readonly ReadSetupStatus _readSetupStatus;

    public SetupController(ReadSetupStatus readSetupStatus)
    {
        _readSetupStatus = readSetupStatus;
    }

    [HttpGet("status")]
    public async Task<ActionResult<SetupStatusResponse>> GetStatus(CancellationToken cancellationToken)
    {
        var status = await _readSetupStatus.ExecuteAsync(cancellationToken);
        return Ok(new SetupStatusResponse(status.SetupComplete, status.IsOnline, status.RequiredStep));
    }
}
