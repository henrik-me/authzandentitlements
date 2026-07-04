using System.Text.RegularExpressions;
using AuthzEntitlements.Authz.Pdp.Catalog;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Lifecycle;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Golden-decision snapshot + drift detection (CS17): the committed, reviewed baseline for every
// catalog scenario (decision + reason + obligations). Covers snapshot/catalog alignment, every
// RBAC engine matching the baseline (no drift), non-vacuous drift detection, missing/extra
// scenario detection, the pinned threshold obligations, and the stable policy-version hash.
public sealed class GoldenDecisionTests
{
    [Fact]
    public void Golden_HasOneEntryPerCatalogScenario()
    {
        var catalogIds = FintechScenarioCatalog.Scenarios
            .Select(s => s.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
        var goldenIds = GoldenDecisionSnapshot.Golden
            .Select(g => g.ScenarioId).OrderBy(id => id, StringComparer.Ordinal).ToList();

        Assert.Equal(catalogIds, goldenIds);
    }

    [Fact]
    public void Golden_HasNoDuplicateScenarioIds()
    {
        var duplicates = GoldenDecisionSnapshot.Golden
            .GroupBy(g => g.ScenarioId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("aspnet")]
    [InlineData("casbin")]
    [InlineData("cedar")]
    public void RbacEngine_MatchesGolden_NoDrift(string engine)
    {
        var provider = LifecycleTestSupport.ProviderByName(engine);

        var computed = GoldenDecisionSnapshot.Compute(provider, FintechScenarioCatalog.Scenarios);
        var drift = GoldenDecisionSnapshot.Diff(GoldenDecisionSnapshot.Golden, computed);

        Assert.Empty(drift);
    }

    [Fact]
    public void Diff_DetectsDrift_WhenEngineDiverges()
    {
        var alwaysPermit = new FixedProvider(
            "drifted", AccessDecision.Permit(new Reason(ReasonCodes.Permit, "forced")));

        var computed = GoldenDecisionSnapshot.Compute(alwaysPermit, FintechScenarioCatalog.Scenarios);
        var drift = GoldenDecisionSnapshot.Diff(GoldenDecisionSnapshot.Golden, computed);

        // Every deny scenario in the golden must surface as drift against a forced-permit engine.
        var denyScenarios = GoldenDecisionSnapshot.Golden.Count(g => g.Decision == Decision.Deny);
        Assert.NotEmpty(drift);
        Assert.True(drift.Count >= denyScenarios);
    }

    [Fact]
    public void Diff_DetectsMissingScenario()
    {
        var current = GoldenDecisionSnapshot.Golden.Skip(1).ToList();

        var drift = GoldenDecisionSnapshot.Diff(GoldenDecisionSnapshot.Golden, current);

        var dropped = GoldenDecisionSnapshot.Golden[0].ScenarioId;
        Assert.Contains(drift, line => line.Contains(dropped, StringComparison.Ordinal) && line.Contains("missing", StringComparison.Ordinal));
    }

    [Fact]
    public void Diff_DetectsExtraScenario()
    {
        var current = GoldenDecisionSnapshot.Golden
            .Append(new GoldenDecision("phantom-scenario", Decision.Permit, ReasonCodes.Permit, []))
            .ToList();

        var drift = GoldenDecisionSnapshot.Diff(GoldenDecisionSnapshot.Golden, current);

        Assert.Contains(drift, line => line.Contains("phantom-scenario", StringComparison.Ordinal));
    }

    [Fact]
    public void Diff_OfIdenticalSnapshots_IsEmpty()
    {
        var drift = GoldenDecisionSnapshot.Diff(GoldenDecisionSnapshot.Golden, GoldenDecisionSnapshot.Golden);

        Assert.Empty(drift);
    }

    [Theory]
    [InlineData("teller-create-small-txn", ObligationIds.PostImmediately)]
    [InlineData("teller-create-large-txn", ObligationIds.RequireApproval)]
    [InlineData("teller-create-threshold-boundary", ObligationIds.RequireApproval)]
    public void Golden_PinsThresholdObligation(string scenarioId, string expectedObligation)
    {
        var golden = GoldenDecisionSnapshot.Golden.Single(g => g.ScenarioId == scenarioId);

        Assert.Equal(Decision.Permit, golden.Decision);
        Assert.Equal([expectedObligation], golden.ObligationIds);
    }

    [Fact]
    public void Version_IsStableLowercaseSha256Hex()
    {
        Assert.Matches(new Regex("^[0-9a-f]{64}$"), GoldenDecisionSnapshot.Version);
    }
}
