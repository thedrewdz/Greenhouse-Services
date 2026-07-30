using System.Text.Json;
using System.Text.Json.Serialization;

namespace Greenhouse.Core.EdgeUnits;

/// <summary>
/// The parts of a <c>gh/heartbeat</c> payload the Main Unit needs for registration and topology
/// decisions. Telemetry values are deliberately not modelled here.
/// </summary>
/// <param name="DeviceId">Edge Unit hardware identity.</param>
/// <param name="Slots">
/// Reported slot topology, or <c>null</c> when the payload omitted it. The bootstrap heartbeat
/// that completes onboarding is allowed to carry only <c>id</c> and <c>device_id</c>, so an
/// absent topology must never be read as "no slots".
/// </param>
public sealed record HeartbeatMessage(string DeviceId, IReadOnlyList<HeartbeatSlot>? Slots)
{
    /// <summary>
    /// Parses <paramref name="payload"/>, returning <c>null</c> when it is malformed or missing
    /// the required <c>device_id</c>. Malformed payloads are diagnostics, never exceptions.
    /// </summary>
    public static HeartbeatMessage? TryParse(string payload)
    {
        HeartbeatDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<HeartbeatDto>(payload);
        }
        catch (JsonException)
        {
            return null;
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.DeviceId))
        {
            return null;
        }

        var slots = dto.Slots?
            .Where(slot => slot.I2cAddress is not null)
            .Select(slot => new HeartbeatSlot(slot.SlotId, slot.I2cAddress!, slot.Capability))
            .OrderBy(slot => slot.SlotId)
            .ToArray();

        return new HeartbeatMessage(dto.DeviceId, slots);
    }

    private sealed record HeartbeatDto(
        [property: JsonPropertyName("device_id")] string? DeviceId,
        [property: JsonPropertyName("slots")] IReadOnlyList<HeartbeatSlotDto>? Slots);

    private sealed record HeartbeatSlotDto(
        [property: JsonPropertyName("slot_id")] int SlotId,
        [property: JsonPropertyName("i2c_address")] string? I2cAddress,
        [property: JsonPropertyName("capability")] string? Capability);
}

/// <summary>One slot as reported by an Edge Unit heartbeat.</summary>
/// <param name="SlotId">Slot index.</param>
/// <param name="I2cAddress">Module I2C address — the module identity used for drift detection.</param>
/// <param name="Capability">Capability the module reports, before any operator mapping.</param>
public sealed record HeartbeatSlot(int SlotId, string I2cAddress, string? Capability);
