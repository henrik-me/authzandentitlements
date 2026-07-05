using AuthzEntitlements.Authz.Pdp.Catalog;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Topaz;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Integration tests against a REAL Topaz authorizer loading the SHARED OPA bundle (infra/opa/policy) via
// infra/topaz/config.yaml. They SOFT-SKIP (early return, no failure) when TOPAZ_TEST_ENDPOINT is unset,
// so the default `dotnet test` — which has no Docker/Topaz — stays green. To run them: start the `topaz`
// container (or any Topaz authorizer serving the bank bundle) and set
// TOPAZ_TEST_ENDPOINT=https://localhost:8282. No Testcontainers / extra NuGet dependency is used.
//
// The parity bar is the SHARED FintechScenarioCatalog — the very same scenarios the reference engine and
// the OPA adapter answer — so a green run here is the head-to-head "OPA standalone vs OPA-inside-Topaz":
// the OPA policy running INSIDE Topaz reproduces the reference Decision AND primary reason code for every
// fintech scenario. Because the bundle path is CI-invisible without a container, this env-gated suite is
// the ONLY proof of full policy parity; the offline TopazDecisionProviderTests prove the mapping/
// fail-closed layer above it.
public sealed class TopazIntegrationTests
{
    private static TopazDecisionProvider? ProviderOrSkip(out TopazCheckService? service)
    {
        service = null;
        var endpoint = Environment.GetEnvironmentVariable("TOPAZ_TEST_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null; // Soft-skip: no server configured (blank or whitespace-only).
        }

        service = new TopazCheckService(
            Options.Create(new TopazOptions { Endpoint = endpoint }), NullLoggerFactory.Instance);
        return new TopazDecisionProvider(service, NullLogger<TopazDecisionProvider>.Instance);
    }

    [Fact]
    public void FullCatalog_MatchesReferenceDecisionAndReasonParity()
    {
        var provider = ProviderOrSkip(out var service);
        using (service)
        {
            if (provider is null) { return; }

            var report = ScenarioCatalogRunner.Run(FintechScenarioCatalog.Scenarios, provider);

            var failures = report.Results
                .Where(r => !r.Passed)
                .Select(r =>
                    $"{r.Scenario.Id}: expected {r.Scenario.Expected}/{r.Scenario.ExpectedReasonCode} " +
                    $"got {r.Actual.Decision}/" +
                    $"{(r.Actual.Reasons.Count > 0 ? r.Actual.Reasons[0].Code : "(none)")}")
                .ToList();

            Assert.True(
                report.AllPassed,
                $"{report.Passed}/{report.Total} scenarios passed. Failures:\n{string.Join("\n", failures)}");
        }
    }

    [Fact]
    public void BlankEndpoint_FailsClosed_WithoutServer()
    {
        // This one does not need a server: it asserts the fail-closed behaviour when Endpoint is blank —
        // the provider must DENY (ProviderUnavailable), never throw, when the engine is unconfigured.
        using var service = new TopazCheckService(
            Options.Create(new TopazOptions { Endpoint = "" }), NullLoggerFactory.Instance);
        var provider = new TopazDecisionProvider(service, NullLogger<TopazDecisionProvider>.Instance);

        var decision = provider.Evaluate(FintechScenarioCatalog.Scenarios[0].Request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal("ProviderUnavailable", decision.Reasons[0].Code);
    }
}
