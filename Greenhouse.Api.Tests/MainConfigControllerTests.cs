using Greenhouse.Api.Contracts;
using Greenhouse.Api.Controllers;
using Greenhouse.Core.Configuration;
using Greenhouse.Core.Setup;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Greenhouse.Api.Tests;

public class MainConfigControllerTests
{
    private static MainConfigController Create(FakeMainConfigRepository repository)
    {
        var readMainConfig = new ReadMainConfig(repository);
        var writeMainConfig = new WriteMainConfig(repository, TimeProvider.System);
        return new MainConfigController(readMainConfig, writeMainConfig);
    }

    [Fact]
    public async Task Get_returns_404_when_no_config()
    {
        var result = await Create(new FakeMainConfigRepository()).Get(CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Get_returns_200_with_body_when_config_exists()
    {
        var repository = new FakeMainConfigRepository
        {
            Current = new MainConfig("North", "Block A", "desc", DateTime.UnixEpoch, DateTime.UnixEpoch),
        };

        var result = await Create(repository).Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<MainConfigResponse>(ok.Value);
        Assert.Equal("North", body.GreenhouseName);
    }

    [Fact]
    public async Task Post_returns_201_with_body_on_first_create()
    {
        var result = await Create(new FakeMainConfigRepository())
            .Post(new MainConfigRequest("North", "Block A", "desc"), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        var body = Assert.IsType<MainConfigResponse>(created.Value);
        Assert.Equal("North", body.GreenhouseName);
    }

    [Fact]
    public async Task Post_returns_409_when_config_already_exists()
    {
        var repository = new FakeMainConfigRepository
        {
            Current = new MainConfig("Existing", "Loc", null, DateTime.UnixEpoch, DateTime.UnixEpoch),
        };

        var result = await Create(repository)
            .Post(new MainConfigRequest("North", "Block A", null), CancellationToken.None);

        Assert.IsType<ConflictResult>(result);
    }

    [Fact]
    public async Task Post_returns_422_envelope_when_name_missing()
    {
        var result = await Create(new FakeMainConfigRepository())
            .Post(new MainConfigRequest("  ", "Block A", null), CancellationToken.None);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        var envelope = Assert.IsType<ValidationErrorEnvelope>(unprocessable.Value);
        Assert.Equal("validation-error", envelope.Type);
        Assert.True(envelope.Errors.ContainsKey("greenhouseName"));
    }

    [Fact]
    public async Task Post_returns_422_when_field_too_long()
    {
        var longLocation = new string('x', MainConfigValidation.LocationMaxLength + 1);

        var result = await Create(new FakeMainConfigRepository())
            .Post(new MainConfigRequest("North", longLocation, null), CancellationToken.None);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        var envelope = Assert.IsType<ValidationErrorEnvelope>(unprocessable.Value);
        Assert.True(envelope.Errors.ContainsKey("location"));
    }

    [Fact]
    public void Put_returns_501()
    {
        var result = Assert.IsType<StatusCodeResult>(Create(new FakeMainConfigRepository()).Put());
        Assert.Equal(StatusCodes.Status501NotImplemented, result.StatusCode);
    }

    [Fact]
    public void Delete_returns_501()
    {
        var result = Assert.IsType<StatusCodeResult>(Create(new FakeMainConfigRepository()).Delete());
        Assert.Equal(StatusCodes.Status501NotImplemented, result.StatusCode);
    }
}
