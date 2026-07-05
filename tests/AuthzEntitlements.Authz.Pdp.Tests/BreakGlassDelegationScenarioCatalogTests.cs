using AuthzEntitlements.Authz.Pdp.Catalog;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Runs the CS21 break-glass + delegation scenario catalog against the reference provider and asserts
// every scenario yields its expected decision + primary reason code (the whole catalog is green), plus
// per-scenario named cases and catalog well-formedness. Mirrors AgentAccessScenarioCatalogTests so the
// CS21 catalog gets the same coverage discipline as the CS19 OBO catalog.
public sealed class BreakGlassDelegationScenarioCatalogTests
{
    public static IEnumerable<object[]> ScenarioIds() =>
        BreakGlassDelegationScenarioCatalog.Scenarios.Select(scenario => new object[] { scenario.Id });

    [Fact]
    public void ReferenceProvider_AnswersFullBreakGlassDelegationCatalog()
    {
        var report = ScenarioCatalogRunner.Run(
            BreakGlassDelegationScenarioCatalog.Scenarios, new ReferenceDecisionProvider());

        var failing = report.Results
            .Where(result => !result.Passed)
            .Select(result => result.Scenario.Id)
            .ToList();

        Assert.True(report.AllPassed, $"Failing scenarios: {string.Join(", ", failing)}");
        Assert.Equal(report.Total, report.Passed);
        Assert.Equal(BreakGlassDelegationScenarioCatalog.Scenarios.Count, report.Total);
    }

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void ReferenceProvider_MatchesScenarioExpectation(string scenarioId)
    {
        var scenario = BreakGlassDelegationScenarioCatalog.Scenarios.Single(s => s.Id == scenarioId);
        var provider = new ReferenceDecisionProvider();

        var decision = provider.Evaluate(scenario.Request);

        Assert.Equal(scenario.Expected, decision.Decision);
        Assert.Equal(scenario.ExpectedReasonCode, decision.Reasons[0].Code);
    }

    [Fact]
    public void Catalog_HasNoDuplicateScenarioIds()
    {
        var duplicates = BreakGlassDelegationScenarioCatalog.Scenarios
            .GroupBy(scenario => scenario.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Catalog_HasAtLeastEightScenarios()
    {
        Assert.True(
            BreakGlassDelegationScenarioCatalog.Scenarios.Count >= 8,
            $"Expected at least 8 scenarios, found {BreakGlassDelegationScenarioCatalog.Scenarios.Count}.");
    }

    [Fact]
    public void Catalog_CoversElevation_IntegrityGuard_Delegation_And_HumanControl()
    {
        var scenarios = BreakGlassDelegationScenarioCatalog.Scenarios;

        // Break-glass elevates a missing-capability deny to a BreakGlassInvoked permit.
        Assert.Contains(scenarios, s =>
            s.Expected == Decision.Permit && s.ExpectedReasonCode == ReasonCodes.BreakGlassInvoked);

        // Break-glass never overrides an integrity invariant (tenant + SoD both proven).
        Assert.Contains(scenarios, s =>
            s.Expected == Decision.Deny && s.ExpectedReasonCode == ReasonCodes.TenantMismatch);
        Assert.Contains(scenarios, s =>
            s.Expected == Decision.Deny && s.ExpectedReasonCode == ReasonCodes.MakerEqualsChecker);

        // Delegation: an active grant permits, an inactive/mismatched grant denies DelegationNotActive.
        Assert.Contains(scenarios, s =>
            s.Expected == Decision.Permit
            && s.ExpectedReasonCode == ReasonCodes.Permit
            && s.Request.Subject.Actor is not null
            && s.Request.Context.Delegation is not null);
        Assert.Contains(scenarios, s =>
            s.Expected == Decision.Deny && s.ExpectedReasonCode == ReasonCodes.DelegationNotActive);

        // A control row proves the human/no-context path (no grants, no injected clock) is unchanged.
        Assert.Contains(scenarios, s =>
            s.Expected == Decision.Permit
            && s.ExpectedReasonCode == ReasonCodes.Permit
            && s.Request.Subject.Actor is null
            && s.Request.Context.BreakGlass is null
            && s.Request.Context.Delegation is null
            && s.Request.Context.Now is null);
    }
}
