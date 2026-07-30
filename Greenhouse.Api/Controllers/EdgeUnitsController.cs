using Greenhouse.Api.Contracts;
using Greenhouse.Core.EdgeUnits;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Greenhouse.Api.Controllers;

/// <summary>
/// The registered Edge Unit resources. <c>PUT .../mapping</c> is shared by all three mapping
/// flows — initial onboarding mapping, operator reconfiguration, and topology-drift
/// reconfiguration — and behaves identically for each.
/// </summary>
[ApiController]
[Route("api/edge-units")]
public sealed class EdgeUnitsController : ControllerBase
{
    private readonly IEdgeUnitRepository _edgeUnits;
    private readonly UpdateEdgeUnitMapping _updateMapping;

    public EdgeUnitsController(IEdgeUnitRepository edgeUnits, UpdateEdgeUnitMapping updateMapping)
    {
        _edgeUnits = edgeUnits;
        _updateMapping = updateMapping;
    }

    [HttpGet]
    [ProducesResponseType(typeof(EdgeUnitListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<EdgeUnitListResponse>> GetAll(CancellationToken cancellationToken)
    {
        var units = await _edgeUnits.GetAllAsync(cancellationToken);
        return Ok(new EdgeUnitListResponse(units.Select(EdgeUnitSummaryResponse.From).ToArray()));
    }

    [HttpGet("{deviceId}")]
    [ProducesResponseType(typeof(EdgeUnitDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EdgeUnitDetailResponse>> Get(string deviceId, CancellationToken cancellationToken)
    {
        var unit = await _edgeUnits.GetAsync(deviceId, cancellationToken);
        return unit is null ? NotFound() : Ok(EdgeUnitDetailResponse.From(unit));
    }

    /// <summary>
    /// Stores a runtime mapping. On success the configuration is published to
    /// <c>ghcfg/wr-{device_id}</c> asynchronously; publish and acknowledgement progress is
    /// observed through the onboarding hub and the unit's <c>mappingStatus</c>.
    /// </summary>
    [HttpPut("{deviceId}/mapping")]
    [ProducesResponseType(typeof(EdgeUnitMappingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ValidationErrorEnvelope), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> PutMapping(
        string deviceId,
        [FromBody] EdgeUnitMappingRequest request,
        CancellationToken cancellationToken)
    {
        var mapping = new EdgeUnitMapping(
            request.UnitName ?? string.Empty,
            request.Location ?? string.Empty,
            (request.Slots ?? Array.Empty<SlotMappingRequest>())
                .Select(slot => new SlotMapping(
                    slot.SlotId,
                    slot.Role ?? string.Empty,
                    slot.Capability ?? string.Empty,
                    slot.Label))
                .ToArray());

        var result = await _updateMapping.ExecuteAsync(deviceId, mapping, cancellationToken);

        return result switch
        {
            UpdateMappingResult.Accepted accepted =>
                Ok(EdgeUnitMappingResponse.From(accepted.EdgeUnit)),
            UpdateMappingResult.UnknownDevice => NotFound(),
            UpdateMappingResult.Invalid invalid =>
                UnprocessableEntity(ValidationErrorEnvelope.From(invalid.Errors)),
            _ => throw new InvalidOperationException("Unexpected mapping result."),
        };
    }
}
