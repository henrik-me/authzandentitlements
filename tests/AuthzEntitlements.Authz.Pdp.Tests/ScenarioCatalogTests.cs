using AuthzEntitlements.Authz.Pdp.Catalog;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// The CS05 exit criterion: "a reference provider answers the full scenario catalog."
// (a) the whole catalog passes; (b) each scenario is an individually-named case asserting
// the decision AND primary reason code; (c) the catalog itself is well-formed (no duplicate
// ids, a meaningful floor size); plus a meta-check that a green run is a real comparison.
public sealed class ScenarioCatalogTests
{
    public static IEnumerable<object[]> ScenarioIds() =>
        FintechScenarioCatalog.Scenarios.Select(scenario => new object[] { scenario.Id });

    [Fact]
    public void ReferenceProvider_AnswersFullCatalog()
    {
        var report = ScenarioCatalogRunner.Run(
            FintechScenarioCatalog.Scenarios, new ReferenceDecisionProvider());

        var failing = report.Results
            .Where(result => !result.Passed)
            .Select(result => result.Scenario.Id)
            .ToList();

        Assert.True(report.AllPassed, $"Failing scenarios: {string.Join(", ", failing)}");
        Assert.Equal(report.Total, report.Passed);

        var expectedTotal = FintechScenarioCatalog.Scenarios.Count;
        Assert.Equal(expectedTotal, report.Total);
    }

    [Theory]
    [MemberData(nameof(ScenarioIds))]
    public void ReferenceProvider_MatchesScenarioExpectation(string scenarioId)
    {
        var scenario = FintechScenarioCatalog.Scenarios.Single(s => s.Id == scenarioId);
        var provider = new ReferenceDecisionProvider();

        var decision = provider.Evaluate(scenario.Request);

        Assert.Equal(scenario.Expected, decision.Decision);
        Assert.Equal(scenario.ExpectedReasonCode, decision.Reasons[0].Code);
    }

    [Fact]
    public void Catalog_HasNoDuplicateScenarioIds()
    {
        var duplicates = FintechScenarioCatalog.Scenarios
            .GroupBy(scenario => scenario.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Catalog_HasAtLeastSixteenScenarios()
    {
        var count = FintechScenarioCatalog.Scenarios.Count;
        Assert.True(count >= 16, $"Expected at least 16 scenarios, found {count}.");
    }

    [Fact]
    public void ScenarioCatalogRunner_DelegateOverload_MatchesProviderOverload()
    {
        var provider = new ReferenceDecisionProvider();

        var viaDelegate = ScenarioCatalogRunner.Run(
            FintechScenarioCatalog.Scenarios, provider.Evaluate);

        Assert.True(viaDelegate.AllPassed);
        Assert.Equal(viaDelegate.Total, viaDelegate.Passed);
    }

    [Fact]
    public void ScenarioCatalogRunner_FlagsFailures_WhenEvaluatorIsWrong()
    {
        // A pure always-permit evaluator must fail every deny scenario — proving a green
        // catalog run is a real comparison, not vacuously true.
        static AccessDecision AlwaysPermit(AccessRequest _) =>
            AccessDecision.Permit(new Reason(ReasonCodes.Permit, "forced permit"));

        var report = ScenarioCatalogRunner.Run(FintechScenarioCatalog.Scenarios, AlwaysPermit);

        Assert.False(report.AllPassed);

        var permitScenarios = FintechScenarioCatalog.Scenarios
            .Count(scenario => scenario.Expected == Decision.Permit);
        Assert.Equal(permitScenarios, report.Passed);
    }

    [Fact]
    public void Catalog_CoversBothPermitAndDenyOutcomes()
    {
        var permits = FintechScenarioCatalog.Scenarios.Count(s => s.Expected == Decision.Permit);
        var denies = FintechScenarioCatalog.Scenarios.Count(s => s.Expected == Decision.Deny);

        Assert.True(permits > 0, "Catalog should exercise at least one permit outcome.");
        Assert.True(denies > 0, "Catalog should exercise at least one deny outcome.");
    }
}
