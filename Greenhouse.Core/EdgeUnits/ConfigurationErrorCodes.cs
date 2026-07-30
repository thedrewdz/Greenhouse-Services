namespace Greenhouse.Core.EdgeUnits;

/// <summary>
/// The Phase 1 runtime-configuration error code set and the Main Unit behaviour each one drives
/// (<c>specs/edge-unit-configuration/spec.md</c>).
/// </summary>
public static class ConfigurationErrorCodes
{
    public const int Success = 0;
    public const int UnsupportedSchemaVersion = 3001;
    public const int DeviceIdMismatch = 3002;
    public const int InvalidMappingPayload = 3003;
    public const int MappingVersionConflict = 3004;
    public const int InternalApplyError = 3099;

    /// <summary>
    /// True when the Main Unit may spend another attempt from the retry budget. Only an
    /// internal apply error is transient — the others describe a payload or version problem that
    /// resending the identical message cannot fix.
    /// </summary>
    public static bool IsRetryable(int errorCode) => errorCode == InternalApplyError;
}
