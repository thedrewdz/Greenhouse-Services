namespace Greenhouse.Core.Tests;

/// <summary>
/// THROWAWAY PROOF ARTIFACT — MUST NEVER MERGE.
///
/// Exists only to make <c>build-and-test</c> go red on a pull request, so that
/// Greenhouse-Services#58 AC2 ("a pull request with a red build-and-test cannot be
/// merged") can be <em>demonstrated</em> rather than asserted. The branch carrying
/// this file (<c>update/ci-gate-proof</c>) is deleted once the blocked merge is
/// captured on the issue.
///
/// If you are reading this on <c>main</c>, the merge gate failed and #58 should be
/// reopened.
/// </summary>
public class CiGateProofTests
{
    [Fact]
    public void Deliberate_failure_to_prove_the_merge_gate_bites()
    {
        Assert.True(false, "Intentional failure — proving the required check blocks a merge (Greenhouse-Services#58 AC2).");
    }
}
