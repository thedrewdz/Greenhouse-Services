using System.Text.Json.Serialization;

namespace Greenhouse.Api.Contracts;

/// <summary>Response for <c>GET /api/setup/status</c>.</summary>
public sealed record SetupStatusResponse(bool SetupComplete, bool IsOnline, string? RequiredStep);

/// <summary>Request body for <c>POST</c>/<c>PUT /api/setup/main-config</c>.</summary>
public sealed record MainConfigRequest(string? GreenhouseName, string? Location, string? Description);

/// <summary>Response for MainConfig reads and successful writes.</summary>
public sealed record MainConfigResponse(
    string GreenhouseName,
    string Location,
    string? Description,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Request body for <c>POST /api/setup/wifi-config</c>. Never persisted as-is.</summary>
public sealed record WifiConfigRequest(string? NetworkName, string? Password);

/// <summary>Response for <c>GET /api/setup/wifi-config</c>. Never includes credentials.</summary>
public sealed record WifiStatusResponse(bool IsOnline, string? NetworkName);

/// <summary>Response for <c>POST /api/setup/wifi-config</c>.</summary>
public sealed record WifiConnectResponse(
    bool Connected,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ErrorMessage = null);

/// <summary>
/// Canonical validation error envelope: <c>{ "type": "validation-error", "errors": { field: [..] } }</c>.
/// </summary>
public sealed record ValidationErrorEnvelope(string Type, IReadOnlyDictionary<string, string[]> Errors)
{
    public static ValidationErrorEnvelope From(IEnumerable<(string Field, string Message)> errors) =>
        new(
            "validation-error",
            errors
                .GroupBy(e => e.Field)
                .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray()));
}
