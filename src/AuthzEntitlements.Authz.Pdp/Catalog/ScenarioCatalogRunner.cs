using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Catalog;

// Runs the scenario catalog and reports per-scenario pass/fail plus a summary. A scenario
// passes when the actual decision equals Expected AND the primary reason code (Reasons[0])
// equals ExpectedReasonCode. Two overloads: one takes a provider directly (the parity-check
// path adapters and tests use); one takes an evaluate delegate so the endpoint can route
// through PdpDecisionService and keep the audit + OTel hooks firing on every scenario.
public static class ScenarioCatalogRunner
{
    public static ScenarioRunReport Run(
        IReadOnlyList<AuthorizationScenario> scenarios,
        IAuthorizationDecisionProvider provider) =>
        Run(scenarios, provider.Evaluate);

    public static ScenarioRunReport Run(
        IReadOnlyList<AuthorizationScenario> scenarios,
        Func<AccessRequest, AccessDecision> evaluate)
    {
        var results = new List<ScenarioResult>(scenarios.Count);
        foreach (var scenario in scenarios)
        {
            var actual = evaluate(scenario.Request);
            var primaryReason = actual.Reasons.Count > 0 ? actual.Reasons[0].Code : string.Empty;
            var passed = actual.Decision == scenario.Expected
                && string.Equals(primaryReason, scenario.ExpectedReasonCode, StringComparison.Ordinal);
            results.Add(new ScenarioResult(scenario, actual, passed));
        }

        var passedCount = results.Count(r => r.Passed);
        return new ScenarioRunReport(results, passedCount == results.Count, passedCount, results.Count);
    }
}
