namespace Greenhouse.Core.Onboarding;

/// <summary>
/// Outcome of a provisioning attempt. A closed hierarchy: exactly one of <see cref="Success"/>
/// or <see cref="Failed"/>. Error codes mirror the canonical Phase 1 set in
/// <c>specs/edge-unit-onboarding/spec.md</c> (e.g. 2001 unsupported schema, 2003 empty SSID,
/// 2004 invalid broker URI, 2099 internal persistence error).
/// </summary>
public abstract record ProvisioningResult
{
    private ProvisioningResult()
    {
    }

    /// <summary>The Edge Unit accepted and persisted the provisioning payload.</summary>
    public sealed record Success : ProvisioningResult;

    /// <summary>The Edge Unit rejected the payload, or provisioning failed.</summary>
    /// <param name="ErrorCode">Stable non-zero error code from the onboarding spec.</param>
    /// <param name="ErrorMessage">Short human-readable diagnostic for local UI.</param>
    public sealed record Failed(int ErrorCode, string ErrorMessage) : ProvisioningResult;
}
