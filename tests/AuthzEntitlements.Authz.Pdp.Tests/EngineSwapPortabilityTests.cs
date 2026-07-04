using AuthzEntitlements.Authz.Pdp.Catalog;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Lifecycle;
using AuthzEntitlements.Authz.Pdp.Providers;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// CS20 extensibility demonstrations: (D1) swapping the active engine is a config change only —
// one unchanged call site decides through whichever engine "Pdp:Provider" names; and (D3) the
// dual-run parity gate proves two engines agree before a migration is trusted, and would catch
// a genuine divergence if they did not.
public sealed class EngineSwapPortabilityTests
{
    // The single, engine-agnostic call site every D1 case invokes: it never mentions a concrete
    // engine, so proving it returns the configured engine's decision proves the swap needs no
    // calling-code change.
    private static AccessDecision DecideVia(AuthorizationDecisionProviderFactory factory, AccessRequest request) =>
        factory.GetActiveProvider().Evaluate(request);

    private static AuthorizationDecisionProviderFactory ConfiguredFactory(string provider) =>
        LifecycleTestSupport.Factory(provider, LifecycleTestSupport.RbacProviders());

    [Theory]
    [InlineData("reference")]
    [InlineData("casbin")]
    [InlineData("cedar")]
    [InlineData("aspnet")]
    public void ConfigSwap_SameCallSite_RoutesToConfiguredEngine(string provider)
    {
        var factory = ConfiguredFactory(provider);

        var active = factory.GetActiveProvider();
        var decision = DecideVia(factory, LifecycleTestSupport.PermitLargeTxn());

        Assert.Equal(provider, active.Name);
        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Contains(ObligationIds.RequireApproval, decision.Obligations.Select(o => o.Id));
    }

    [Fact]
    public void ConfigSwap_OnlyConfigChanges_AcrossEveryRbacEngine()
    {
        var request = LifecycleTestSupport.DenyRequest();

        // Iterating the config value alone, through one unchanged DecideVia call, yields each engine
        // in turn — the app code is identical for every engine.
        foreach (var provider in LifecycleTestSupport.RbacEngineNames)
        {
            var factory = ConfiguredFactory(provider);

            Assert.Equal(provider, factory.GetActiveProvider().Name);
            Assert.Equal(Decision.Deny, DecideVia(factory, request).Decision);
        }
    }

    [Theory]
    [InlineData("casbin")]
    [InlineData("cedar")]
    [InlineData("aspnet")]
    public void DualRun_ReferenceVsRbacEngine_HasZeroDivergences(string candidate)
    {
        var runner = new ShadowRunner(LifecycleTestSupport.RbacFactory());

        var report = runner.RunCatalog("reference", candidate, FintechScenarioCatalog.Scenarios);

        Assert.True(report.AllAgree,
            $"Divergences: {string.Join("; ", report.Divergences.Select(d => d.ScenarioId))}");
        Assert.Empty(report.Divergences);
        Assert.Equal(report.Total, report.Agreements);
    }

    [Fact]
    public void DualRun_WouldCatchDivergence_WhenAnEngineDisagrees()
    {
        // A drift engine that flips the reference's deny into a permit: the parity gate must NOT be
        // vacuous — it has to surface the decision (and reason) mismatch, or a bad migration slips through.
        var providers = new IAuthorizationDecisionProvider[]
        {
            new ReferenceDecisionProvider(),
            new FixedProvider("drift", AccessDecision.Permit(new Reason(ReasonCodes.Permit, "forced permit"))),
        };
        var runner = new ShadowRunner(LifecycleTestSupport.Factory("reference", providers));

        var result = runner.Run("reference", ["drift"], LifecycleTestSupport.DenyRequest());

        Assert.False(result.AllAgree);
        var comparison = Assert.Single(result.Comparisons);
        Assert.Contains(comparison.Divergences, d => d.StartsWith("decision:", StringComparison.Ordinal));
        Assert.Contains(comparison.Divergences, d => d.StartsWith("reason:", StringComparison.Ordinal));
    }
}
