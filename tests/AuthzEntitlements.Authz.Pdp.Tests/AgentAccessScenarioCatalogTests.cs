using AuthzEntitlements.Authz.Pdp.Catalog;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Runs the CS19 agent/OBO scenario catalog against the reference provider and asserts every
// scenario yields its expected decision + primary reason code (the whole catalog is green),
// plus per-scenario named cases and catalog well-formedness. Mirrors ScenarioCatalogTests so the
// OBO catalog gets the same coverage discipline as the Actor-free parity catalog.
public sealed class AgentAccessScenarioCatalogTests
{
    public static IEnumerable<object[]> ScenarioIds() =>
        AgentAccessScenarioCatalog.Scenarios.Select(scenario => new object[] { scenario.Id });

    [Fact]
    public void ReferenceProvider_AnswersFullAgentCatalog()
    {
        var report = ScenarioCatalogRunner.Run(
            AgentAccessScenarioCatalog.Scenarios, new ReferenceDecisionProvider());

        var failing = report.Results
            .Where(result => !result.Passed)
            .Select(result => result.Scenario.Id)
            .ToList();

        Assert.True(report.AllPassed, $"Failing scenarios: {string.Join(", ", failing)}");
        Assert.Equal(report.Total, report.Passed);
        Assert.Equal(AgentAccessScenarioCatalog.Scenarios.Count, report.Total);
    }

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void ReferenceProvider_MatchesScenarioExpectation(string scenarioId)
    {
        var scenario = AgentAccessScenarioCatalog.Scenarios.Single(s => s.Id == scenarioId);
        var provider = new ReferenceDecisionProvider();

        var decision = provider.Evaluate(scenario.Request);

        Assert.Equal(scenario.Expected, decision.Decision);
        Assert.Equal(scenario.ExpectedReasonCode, decision.Reasons[0].Code);
    }

    [Fact]
    public void Catalog_HasNoDuplicateScenarioIds()
    {
        var duplicates = AgentAccessScenarioCatalog.Scenarios
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
            AgentAccessScenarioCatalog.Scenarios.Count >= 8,
            $"Expected at least 8 scenarios, found {AgentAccessScenarioCatalog.Scenarios.Count}.");
    }

    [Fact]
    public void Catalog_CoversDelegationDeny_HumanPassthroughDeny_And_SelfActingPermit()
    {
        var scenarios = AgentAccessScenarioCatalog.Scenarios;

        // (b) an agent missing the delegated scope for a would-be-permitted action.
        Assert.Contains(scenarios, s =>
            s.Expected == Decision.Deny && s.ExpectedReasonCode == ReasonCodes.DelegationScopeMissing);

        // (c) an agent acting for a not-permitted human denies with the SAME human reason.
        Assert.Contains(scenarios, s =>
            s.Expected == Decision.Deny && s.ExpectedReasonCode == ReasonCodes.TenantMismatch);
        Assert.Contains(scenarios, s =>
            s.Expected == Decision.Deny && s.ExpectedReasonCode == ReasonCodes.RoleNotAuthorized);

        // (d) a non-human subject acting as itself (Type=service, no Actor) permits.
        Assert.Contains(scenarios, s =>
            s.Expected == Decision.Permit
            && s.Request.Subject.Type == "service"
            && s.Request.Subject.Actor is null);

        // (a)/(f) at least one OBO permit that goes through an Actor.
        Assert.Contains(scenarios, s =>
            s.Expected == Decision.Permit && s.Request.Subject.Actor is not null);
    }

    [Fact]
    public void FintechCatalog_StaysActorFree()
    {
        // The cross-engine parity catalog must not carry OBO delegation (that is this catalog's job).
        Assert.All(
            FintechScenarioCatalog.Scenarios,
            scenario => Assert.Null(scenario.Request.Subject.Actor));
    }
}
