using AuthzEntitlements.Authz.Pdp.Contracts;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Covers the CS16 explanation foundation: the shared reason->rule normalization, the baseline
// explanation builder, and the AccessDecision.WithExplanation enrichment helper. These are the
// exact contract shapes the per-engine adapters depend on, so the tests pin their behaviour.
public sealed class DecisionExplanationTests
{
    [Theory]
    [InlineData(ReasonCodes.Permit, DeterminingRules.AllRulesSatisfied)]
    [InlineData(ReasonCodes.MissingScope, DeterminingRules.Scope)]
    [InlineData(ReasonCodes.RoleNotAuthorized, DeterminingRules.Role)]
    [InlineData(ReasonCodes.TenantMismatch, DeterminingRules.Tenant)]
    [InlineData(ReasonCodes.BranchNotInTenant, DeterminingRules.Tenant)]
    [InlineData(ReasonCodes.SubjectNotMaker, DeterminingRules.SubjectIsMaker)]
    [InlineData(ReasonCodes.NotPending, DeterminingRules.PendingStatus)]
    [InlineData(ReasonCodes.MakerEqualsChecker, DeterminingRules.SegregationOfDuties)]
    [InlineData(ReasonCodes.UnknownAction, DeterminingRules.UnknownAction)]
    [InlineData("NoRelationship", DeterminingRules.Relationship)]
    [InlineData("ProviderUnavailable", DeterminingRules.EngineUnavailable)]
    [InlineData("EngineUnavailable", DeterminingRules.EngineUnavailable)]
    [InlineData("something-nobody-recognises", DeterminingRules.EngineUnavailable)]
    public void RuleForReason_MapsReasonCodeToNormalizedRule(string reasonCode, string expectedRule)
    {
        Assert.Equal(expectedRule, DecisionExplanations.RuleForReason(reasonCode));
    }

    [Fact]
    public void Baseline_ForPermit_CarriesEngineMappedRuleReasonCodeReferenceAndNarrative()
    {
        var decision = AccessDecision.Permit(new Reason(ReasonCodes.Permit, "all rules satisfied"));

        var explanation = DecisionExplanations.Baseline("reference", decision);

        Assert.Equal("reference", explanation.Engine);
        Assert.Equal(DeterminingRules.AllRulesSatisfied, explanation.DeterminingRule);
        Assert.Equal("all rules satisfied", explanation.Narrative);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.ReasonCode, reference.Kind);
        Assert.Equal(ReasonCodes.Permit, reference.Reference);
        Assert.Null(reference.Detail);
    }

    [Fact]
    public void Baseline_ForDeny_CarriesEngineMappedRuleReasonCodeReferenceAndNarrative()
    {
        var decision = AccessDecision.Deny(new Reason(ReasonCodes.TenantMismatch, "tenant mismatch"));

        var explanation = DecisionExplanations.Baseline("opa", decision);

        Assert.Equal("opa", explanation.Engine);
        Assert.Equal(DeterminingRules.Tenant, explanation.DeterminingRule);
        Assert.Equal("tenant mismatch", explanation.Narrative);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.ReasonCode, reference.Kind);
        Assert.Equal(ReasonCodes.TenantMismatch, reference.Reference);
    }

    [Fact]
    public void WithExplanation_SetsExplanation_AndLeavesDecisionReasonsObligationsUnchanged()
    {
        var original = AccessDecision.Permit(
            new Reason(ReasonCodes.Permit, "ok"),
            new Obligation(ObligationIds.RequireApproval));
        var explanation = DecisionExplanations.Baseline("reference", original);

        var enriched = original.WithExplanation(explanation);

        Assert.Same(explanation, enriched.Explanation);
        Assert.Equal(original.Decision, enriched.Decision);
        Assert.Equal(original.Reasons, enriched.Reasons);
        Assert.Equal(
            original.Obligations.Select(o => o.Id),
            enriched.Obligations.Select(o => o.Id));
    }

    [Fact]
    public void WithExplanation_DoesNotMutateOriginal()
    {
        var original = AccessDecision.Deny(new Reason(ReasonCodes.RoleNotAuthorized, "no role"));

        _ = original.WithExplanation(DecisionExplanations.Baseline("cedar", original));

        Assert.Null(original.Explanation);
    }

    [Fact]
    public void FactoryDecisions_HaveNullExplanation_BeforeEnrichment()
    {
        Assert.Null(AccessDecision.Permit(new Reason(ReasonCodes.Permit, "ok")).Explanation);
        Assert.Null(AccessDecision.Deny(new Reason(ReasonCodes.MissingScope, "no scope")).Explanation);
    }
}
