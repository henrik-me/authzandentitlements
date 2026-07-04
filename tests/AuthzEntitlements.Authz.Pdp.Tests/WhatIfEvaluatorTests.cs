using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Lifecycle;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// What-if simulation (CS17): preview a decision against a chosen engine (or the active one)
// without it being an enforced decision. Covers active-engine fallback, explicit engine
// targeting, reasons/obligations passthrough, deny previews, and the fail-closed unknown engine.
public sealed class WhatIfEvaluatorTests
{
    [Fact]
    public void Evaluate_NullEngine_UsesActiveProvider()
    {
        var evaluator = new WhatIfEvaluator(LifecycleTestSupport.RbacFactory("reference"));

        var result = evaluator.Evaluate(null, LifecycleTestSupport.PermitLargeTxn());

        Assert.Equal("reference", result.Engine);
        Assert.Equal(Decision.Permit, result.Decision);
    }

    [Fact]
    public void Evaluate_BlankEngine_UsesConfiguredActive()
    {
        var evaluator = new WhatIfEvaluator(LifecycleTestSupport.RbacFactory("cedar"));

        var result = evaluator.Evaluate("   ", LifecycleTestSupport.PermitLargeTxn());

        Assert.Equal("cedar", result.Engine);
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("aspnet")]
    [InlineData("casbin")]
    [InlineData("cedar")]
    public void Evaluate_NamedEngine_TargetsThatEngine(string engine)
    {
        var evaluator = new WhatIfEvaluator(LifecycleTestSupport.RbacFactory("reference"));

        var result = evaluator.Evaluate(engine, LifecycleTestSupport.PermitLargeTxn());

        Assert.Equal(engine, result.Engine);
        Assert.Equal(Decision.Permit, result.Decision);
    }

    [Fact]
    public void Evaluate_CarriesReasonsAndObligations()
    {
        var evaluator = new WhatIfEvaluator(LifecycleTestSupport.RbacFactory());

        var result = evaluator.Evaluate("reference", LifecycleTestSupport.PermitLargeTxn());

        Assert.Equal(ReasonCodes.Permit, result.Reasons[0].Code);
        Assert.Contains(result.Obligations, o => o.Id == ObligationIds.RequireApproval);
    }

    [Fact]
    public void Evaluate_DenyScenario_ReturnsDenyWithReason()
    {
        var evaluator = new WhatIfEvaluator(LifecycleTestSupport.RbacFactory());

        var result = evaluator.Evaluate("reference", LifecycleTestSupport.DenyRequest());

        Assert.Equal(Decision.Deny, result.Decision);
        Assert.Equal(ReasonCodes.TenantMismatch, result.Reasons[0].Code);
        Assert.Empty(result.Obligations);
    }

    [Fact]
    public void Evaluate_UnknownEngine_FailsClosed()
    {
        var evaluator = new WhatIfEvaluator(LifecycleTestSupport.RbacFactory());

        var ex = Assert.Throws<InvalidOperationException>(
            () => evaluator.Evaluate("nope", LifecycleTestSupport.PermitLargeTxn()));
        Assert.Contains("nope", ex.Message);
    }
}
