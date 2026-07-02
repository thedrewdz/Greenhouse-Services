using Greenhouse.Api.Contracts;
using Greenhouse.Core.Configuration;
using Greenhouse.Core.Setup;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Greenhouse.Api.Controllers;

[ApiController]
[Route("api/setup/main-config")]
public sealed class MainConfigController : ControllerBase
{
    private readonly ReadMainConfig _readMainConfig;
    private readonly WriteMainConfig _writeMainConfig;

    public MainConfigController(ReadMainConfig readMainConfig, WriteMainConfig writeMainConfig)
    {
        _readMainConfig = readMainConfig;
        _writeMainConfig = writeMainConfig;
    }

    [HttpGet]
    public async Task<ActionResult<MainConfigResponse>> Get(CancellationToken cancellationToken)
    {
        var config = await _readMainConfig.ExecuteAsync(cancellationToken);
        return config is null ? NotFound() : Ok(ToResponse(config));
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] MainConfigRequest request, CancellationToken cancellationToken)
    {
        var errors = MainConfigValidation.Validate(request.GreenhouseName, request.Location, request.Description);
        if (errors.Count > 0)
        {
            return UnprocessableEntity(ValidationErrorEnvelope.From(errors));
        }

        var result = await _writeMainConfig.ExecuteAsync(
            request.GreenhouseName!,
            request.Location!,
            request.Description,
            cancellationToken);

        return result switch
        {
            WriteMainConfigResult.Success success =>
                CreatedAtAction(nameof(Get), null, ToResponse(success.Config)),
            WriteMainConfigResult.AlreadyExists => Conflict(),
            WriteMainConfigResult.ValidationError error =>
                UnprocessableEntity(ValidationErrorEnvelope.From(new[] { (error.Field, error.Message) })),
            _ => throw new InvalidOperationException("Unexpected write result."),
        };
    }

    // Editing config after setup is deferred; the verb is reserved and returns 501 for now.
    [HttpPut]
    public IActionResult Put() => StatusCode(StatusCodes.Status501NotImplemented);

    // Factory-reset deletion is deferred; the verb is reserved and returns 501 for now.
    [HttpDelete]
    public IActionResult Delete() => StatusCode(StatusCodes.Status501NotImplemented);

    private static MainConfigResponse ToResponse(MainConfig config) => new(
        config.GreenhouseName,
        config.Location,
        config.Description,
        config.CreatedAt,
        config.UpdatedAt);
}
