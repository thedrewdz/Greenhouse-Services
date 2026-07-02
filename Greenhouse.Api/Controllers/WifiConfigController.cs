using Greenhouse.Api.Contracts;
using Greenhouse.Core.Networking;
using Greenhouse.Core.Setup;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Greenhouse.Api.Controllers;

[ApiController]
[Route("api/setup/wifi-config")]
public sealed class WifiConfigController : ControllerBase
{
    private const int NetworkNameMaxLength = 100;

    private readonly INetworkConnector _connector;
    private readonly ConnectToNetwork _connectToNetwork;

    public WifiConfigController(INetworkConnector connector, ConnectToNetwork connectToNetwork)
    {
        _connector = connector;
        _connectToNetwork = connectToNetwork;
    }

    [HttpGet]
    public async Task<ActionResult<WifiStatusResponse>> Get(CancellationToken cancellationToken)
    {
        try
        {
            var isOnline = await _connector.IsOnlineAsync(cancellationToken);
            var networkName = await _connector.GetCurrentNetworkNameAsync(cancellationToken);
            return Ok(new WifiStatusResponse(isOnline, networkName));
        }
        catch (NetworkConnectorUnavailableException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] WifiConfigRequest request, CancellationToken cancellationToken)
    {
        var networkName = request.NetworkName?.Trim();
        if (string.IsNullOrEmpty(networkName))
        {
            return UnprocessableEntity(ValidationErrorEnvelope.From(
                new[] { ("networkName", "Network name is required.") }));
        }

        if (networkName.Length > NetworkNameMaxLength)
        {
            return UnprocessableEntity(ValidationErrorEnvelope.From(
                new[] { ("networkName", $"Network name must not exceed {NetworkNameMaxLength} characters.") }));
        }

        try
        {
            var result = await _connectToNetwork.ExecuteAsync(networkName, request.Password, cancellationToken);
            return result switch
            {
                ConnectResult.Connected => Ok(new WifiConnectResponse(true)),
                ConnectResult.Failed failed =>
                    UnprocessableEntity(new WifiConnectResponse(false, failed.ErrorMessage)),
                ConnectResult.TimedOut =>
                    StatusCode(
                        StatusCodes.Status504GatewayTimeout,
                        new WifiConnectResponse(false, "Connection timed out.")),
                _ => throw new InvalidOperationException("Unexpected connect result."),
            };
        }
        catch (NetworkConnectorUnavailableException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
