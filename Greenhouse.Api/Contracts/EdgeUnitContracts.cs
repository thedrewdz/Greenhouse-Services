using Greenhouse.Core.EdgeUnits;

namespace Greenhouse.Api.Contracts;

/// <summary>One entry of <c>GET /api/edge-units</c>.</summary>
public sealed record EdgeUnitSummaryResponse(
    string DeviceId,
    string AdvertisedName,
    string? UnitName,
    string? Location,
    string MappingStatus,
    DateTime? LastHeartbeatAt)
{
    public static EdgeUnitSummaryResponse From(EdgeUnit unit) => new(
        unit.DeviceId,
        unit.AdvertisedName,
        unit.UnitName,
        unit.Location,
        unit.MappingStatus,
        unit.LastHeartbeatAt);
}

/// <summary>Response for <c>GET /api/edge-units</c>.</summary>
public sealed record EdgeUnitListResponse(IReadOnlyList<EdgeUnitSummaryResponse> EdgeUnits);

/// <summary>One slot in <c>GET /api/edge-units/{device_id}</c>.</summary>
public sealed record EdgeUnitSlotResponse(
    int SlotId,
    string? Role,
    string? Capability,
    string? Label,
    string I2cAddress);

/// <summary>Response for <c>GET /api/edge-units/{device_id}</c> — includes last-known topology.</summary>
public sealed record EdgeUnitDetailResponse(
    string DeviceId,
    string AdvertisedName,
    string? UnitName,
    string? Location,
    int MappingVersion,
    string MappingStatus,
    DateTime? LastHeartbeatAt,
    IReadOnlyList<EdgeUnitSlotResponse> Slots)
{
    public static EdgeUnitDetailResponse From(EdgeUnit unit) => new(
        unit.DeviceId,
        unit.AdvertisedName,
        unit.UnitName,
        unit.Location,
        unit.MappingVersion,
        unit.MappingStatus,
        unit.LastHeartbeatAt,
        unit.Slots
            .Select(slot => new EdgeUnitSlotResponse(
                slot.SlotId,
                slot.Role,
                slot.Capability,
                slot.Label,
                slot.I2cAddress))
            .ToArray());
}

/// <summary>Request body for <c>PUT /api/edge-units/{device_id}/mapping</c>.</summary>
public sealed record EdgeUnitMappingRequest(
    string? UnitName,
    string? Location,
    IReadOnlyList<SlotMappingRequest>? Slots);

/// <summary>One slot assignment in a mapping request.</summary>
public sealed record SlotMappingRequest(int SlotId, string? Role, string? Capability, string? Label);

/// <summary>Response for a successful <c>PUT /api/edge-units/{device_id}/mapping</c>.</summary>
public sealed record EdgeUnitMappingResponse(
    string DeviceId,
    string? UnitName,
    string? Location,
    int MappingVersion,
    string MappingStatus)
{
    public static EdgeUnitMappingResponse From(EdgeUnit unit) => new(
        unit.DeviceId,
        unit.UnitName,
        unit.Location,
        unit.MappingVersion,
        unit.MappingStatus);
}
