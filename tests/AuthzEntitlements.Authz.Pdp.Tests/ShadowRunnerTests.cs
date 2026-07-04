using AuthzEntitlements.Authz.Pdp.Catalog;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Lifecycle;
using AuthzEntitlements.Authz.Pdp.Providers;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// The shadow / dual-run comparison harness (CS17): the SAME request (or the whole catalog) run
// against a primary + shadow engines, reporting where they diverge. Covers RBAC-family agreement,
// decision/reason/obligation divergence detection, obligation order-insensitivity, whole-catalog
// parity, and the fail-closed unknown-engine path.
public sealed class ShadowRunnerTests
{
    [Fact]
    public void Run_RbacFamily_AllAgree_OnPermit()
    {
        var runner = new ShadowRunner(LifecycleTestSupport.RbacFactory());

        var result = runner.Run("reference", ["aspnet", "casbin", "cedar"], LifecycleTestSupport.PermitLargeTxn());

        Assert.True(result.AllAgree);
        Assert.Equal(3, result.Comparisons.Count);
        Assert.All(result.Comparisons, c => Assert.True(c.Agrees));
        Assert.All(result.Comparisons, c => Assert.Empty(c.Divergences));
        Assert.Equal(Decision.Permit, result.Primary.Decision);
        Assert.Contains(ObligationIds.RequireApproval, result.Primary.ObligationIds);
    }

    [Fact]
    public void Run_RbacFamily_AllAgree_OnDeny()
    {
        var runner = new ShadowRunner(LifecycleTestSupport.RbacFactory());

        var result = runner.Run("reference", ["aspnet", "casbin", "cedar"], LifecycleTestSupport.DenyRequest());

        Assert.True(result.AllAgree);
        Assert.Equal(Decision.Deny, result.Primary.Decision);
        Assert.Equal(ReasonCodes.TenantMismatch, result.Primary.ReasonCode);
    }

    [Fact]
    public void Run_DetectsDecisionAndReasonDivergence()
    {
        var providers = new IAuthorizationDecisionProvider[]
        {
            new ReferenceDecisionProvider(),
            new FixedProvider("divergent", AccessDecision.Permit(new Reason(ReasonCodes.Permit, "forced permit"))),
        };
        var runner = new ShadowRunner(LifecycleTestSupport.Factory("reference", providers));

        var result = runner.Run("reference", ["divergent"], LifecycleTestSupport.DenyRequest());

        Assert.False(result.AllAgree);
        var comparison = Assert.Single(result.Comparisons);
        Assert.False(comparison.Agrees);
        Assert.Contains(comparison.Divergences, d => d.StartsWith("decision:", StringComparison.Ordinal));
        Assert.Contains(comparison.Divergences, d => d.StartsWith("reason:", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_DetectsObligationDivergence_WhenDecisionAgrees()
    {
        // Both permit, both reason Permit, but different obligations -> only obligations diverge.
        var shadow = AccessDecision.Permit(
            new Reason(ReasonCodes.Permit, "ok"), new Obligation(ObligationIds.PostImmediately));
        var providers = new IAuthorizationDecisionProvider[]
        {
            new ReferenceDecisionProvider(),
            new FixedProvider("wrong-obligation", shadow),
        };
        var runner = new ShadowRunner(LifecycleTestSupport.Factory("reference", providers));

        var result = runner.Run("reference", ["wrong-obligation"], LifecycleTestSupport.PermitLargeTxn());

        var comparison = Assert.Single(result.Comparisons);
        Assert.False(comparison.Agrees);
        Assert.Single(comparison.Divergences);
        Assert.Contains(comparison.Divergences, d => d.StartsWith("obligations:", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_ObligationComparison_IsOrderInsensitive()
    {
        var a = AccessDecision.Permit(new Reason(ReasonCodes.Permit, "a"),
            new Obligation("o1"), new Obligation("o2"));
        var b = AccessDecision.Permit(new Reason(ReasonCodes.Permit, "b"),
            new Obligation("o2"), new Obligation("o1"));
        var providers = new IAuthorizationDecisionProvider[]
        {
            new FixedProvider("a", a),
            new FixedProvider("b", b),
        };
        var runner = new ShadowRunner(LifecycleTestSupport.Factory("a", providers));

        var result = runner.Run("a", ["b"], LifecycleTestSupport.DenyRequest());

        Assert.True(result.AllAgree);
    }

    [Fact]
    public void Run_UnknownEngine_FailsClosed()
    {
        var runner = new ShadowRunner(LifecycleTestSupport.RbacFactory());

        var ex = Assert.Throws<InvalidOperationException>(
            () => runner.Run("reference", ["does-not-exist"], LifecycleTestSupport.DenyRequest()));
        Assert.Contains("does-not-exist", ex.Message);
    }

    [Theory]
    [InlineData("aspnet")]
    [InlineData("casbin")]
    [InlineData("cedar")]
    public void RunCatalog_RbacEngine_MatchesReference_AcrossFullCatalog(string shadow)
    {
        var runner = new ShadowRunner(LifecycleTestSupport.RbacFactory());

        var report = runner.RunCatalog("reference", shadow, FintechScenarioCatalog.Scenarios);

        Assert.True(
            report.AllAgree,
            $"Divergences: {string.Join("; ", report.Divergences.Select(d => $"{d.ScenarioId}:{string.Join(",", d.Divergences)}"))}");
        Assert.Empty(report.Divergences);
        Assert.Equal(FintechScenarioCatalog.Scenarios.Count, report.Total);
        Assert.Equal(report.Total, report.Agreements);
        Assert.Equal("reference", report.Primary);
        Assert.Equal(shadow, report.Shadow);
    }

    [Fact]
    public void RunCatalog_DetectsDivergence_ForAlwaysDenyEngine()
    {
        var providers = new IAuthorizationDecisionProvider[]
        {
            new ReferenceDecisionProvider(),
            new FixedProvider("always-deny",
                AccessDecision.Deny(new Reason(ReasonCodes.UnknownAction, "forced deny"))),
        };
        var runner = new ShadowRunner(LifecycleTestSupport.Factory("reference", providers));

        var report = runner.RunCatalog("reference", "always-deny", FintechScenarioCatalog.Scenarios);

        Assert.False(report.AllAgree);
        // Every permit scenario must diverge (forced-deny disagrees with a real permit).
        var permitScenarios = FintechScenarioCatalog.Scenarios.Count(s => s.Expected == Decision.Permit);
        Assert.True(report.Divergences.Count >= permitScenarios);
        Assert.All(report.Divergences, d => Assert.NotEmpty(d.Divergences));
    }

    [Fact]
    public void DeterministicRbacFamily_IsTheInProcessRbacEngines()
    {
        Assert.Equal(
            new[] { "reference", "aspnet", "casbin", "cedar" },
            ShadowRunner.DeterministicRbacFamily);
    }
}
