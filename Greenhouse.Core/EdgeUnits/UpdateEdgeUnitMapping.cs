using Greenhouse.Core.Onboarding;

namespace Greenhouse.Core.EdgeUnits;

/// <summary>
/// Stores a runtime mapping for an Edge Unit and queues its publication. One use case serves all
/// three callers of <c>PUT /api/edge-units/{device_id}/mapping</c>: initial onboarding mapping,
/// operator-initiated reconfiguration, and topology-drift reconfiguration.
/// </summary>
public sealed class UpdateEdgeUnitMapping
{
    private readonly IEdgeUnitRepository _edgeUnits;
    private readonly IEdgeUnitConfigurationPublisher _publisher;

    public UpdateEdgeUnitMapping(
        IEdgeUnitRepository edgeUnits,
        IEdgeUnitConfigurationPublisher publisher)
    {
        _edgeUnits = edgeUnits;
        _publisher = publisher;
    }

    public async Task<UpdateMappingResult> ExecuteAsync(
        string deviceId,
        EdgeUnitMapping mapping,
        CancellationToken cancellationToken = default)
    {
        var existing = await _edgeUnits.GetAsync(deviceId, cancellationToken);
        if (existing is null)
        {
            return new UpdateMappingResult.UnknownDevice();
        }

        var trimmed = mapping with
        {
            UnitName = mapping.UnitName?.Trim() ?? string.Empty,
            Location = mapping.Location?.Trim() ?? string.Empty,
        };

        var errors = MappingValidation.Validate(trimmed, existing.Slots);
        if (errors.Count > 0)
        {
            // Nothing is published and nothing is written when validation fails, so the operator
            // keeps their entered values and can correct them.
            return new UpdateMappingResult.Invalid(errors);
        }

        var updated = await _edgeUnits.UpdateMappingAsync(deviceId, trimmed, cancellationToken);
        if (updated is null)
        {
            return new UpdateMappingResult.UnknownDevice();
        }

        // First accepted mapping is the initial registration; anything later replaces a mapping
        // the unit already had, which is a topology change from the Edge Unit's point of view.
        var reason = updated.MappingVersion <= 1
            ? MappingReasons.InitialRegistration
            : MappingReasons.TopologyChange;

        _publisher.RequestPublish(deviceId, reason);

        return new UpdateMappingResult.Accepted(updated);
    }
}

/// <summary>
/// Outcome of a mapping submission. A closed hierarchy: exactly one of
/// <see cref="Accepted"/>, <see cref="UnknownDevice"/>, or <see cref="Invalid"/>.
/// </summary>
public abstract record UpdateMappingResult
{
    private UpdateMappingResult()
    {
    }

    /// <summary>Mapping stored; publication is queued.</summary>
    public sealed record Accepted(EdgeUnit EdgeUnit) : UpdateMappingResult;

    /// <summary>No Edge Unit is registered with that device id.</summary>
    public sealed record UnknownDevice : UpdateMappingResult;

    /// <summary>Mapping broke one or more validation rules; nothing was stored or published.</summary>
    public sealed record Invalid(IReadOnlyList<(string Field, string Message)> Errors) : UpdateMappingResult;
}
