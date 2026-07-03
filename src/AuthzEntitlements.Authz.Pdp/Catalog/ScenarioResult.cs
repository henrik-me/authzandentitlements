using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Catalog;

// The outcome of evaluating one scenario against a provider: the scenario, the actual
// decision the provider returned (including obligations, for callers that inspect them),
// and whether it matched the expectation.
public sealed record ScenarioResult(
    AuthorizationScenario Scenario,
    AccessDecision Actual,
    bool Passed);

// The summary of running the whole catalog: every result plus pass counts. AllPassed is
// the single boolean the exit criterion "a reference provider answers the full scenario
// catalog" checks.
public sealed record ScenarioRunReport(
    IReadOnlyList<ScenarioResult> Results,
    bool AllPassed,
    int Passed,
    int Total);
