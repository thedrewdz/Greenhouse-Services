namespace Greenhouse.Core.EdgeUnits;

/// <summary>
/// Canonical <c>mappingStatus</c> values exposed by the Edge Unit API resources
/// (<c>specs/edge-unit-configuration/spec.md</c>). The same literals are persisted, so a
/// stored value round-trips to the API contract without translation.
/// </summary>
public static class MappingStatuses
{
    /// <summary>Registered from a heartbeat; no runtime mapping has been submitted yet.</summary>
    public const string PendingMapping = "pending-mapping";

    /// <summary>Mapping accepted and stored; configuration publish has not completed.</summary>
    public const string PublishPending = "publish-pending";

    /// <summary>Configuration published to the Edge Unit; awaiting its acknowledgement.</summary>
    public const string Published = "published";

    /// <summary>Edge Unit acknowledged the configuration with <c>result=success</c>.</summary>
    public const string Acknowledged = "acknowledged";

    /// <summary>Publish or acknowledgement failed; operator action is required.</summary>
    public const string Failed = "failed";
}
