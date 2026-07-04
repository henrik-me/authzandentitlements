using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// LRN-038: with the IOpenFgaCheckClient seam the OpenFGA adapter's permit/deny DecisionExplanation
// (engine=openfga, DeterminingRule=relationship, the relationship-tuple reference) is now assertable
// OFFLINE — a test double forces allowed=true/false with NO live server. This complements the pure
// OpenFgaProviderMappingTests (mapper + boundary/fail-closed explanations, which never call the
// engine) with the actual permit/deny path CS16 could previously only verify against a running server.
public sealed class OpenFgaProviderSeamTests
{
    private static OpenFgaProvider Provider(IOpenFgaCheckClient client) =>
        new(client, NullLogger<OpenFgaProvider>.Instance);

    private static AccessRequest AccountRead(string subjectId, string? resourceId) =>
        new(
            new Subject("user", subjectId, []),
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account", Id: resourceId),
            new EvaluationContext([]));

    [Fact]
    public void Evaluate_WhenCheckAllowed_Permits_WithRelationshipExplanation()
    {
        var decision = Provider(FakeOpenFgaCheckClient.Allowing())
            .Evaluate(AccountRead("teller1", "acme-checking"));

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.Permit, decision.Reasons[0].Code);

        var explanation = decision.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("openfga", explanation!.Engine);
        Assert.Equal(DeterminingRules.Relationship, explanation.DeterminingRule);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.RelationshipTuple, reference.Kind);
        Assert.Equal("user:teller1#can_view@account:acme-checking", reference.Reference);
    }

    [Fact]
    public void Evaluate_WhenCheckDenied_Denies_NoRelationship_WithRelationshipExplanation()
    {
        var decision = Provider(FakeOpenFgaCheckClient.Denying())
            .Evaluate(AccountRead("carol", "personal-carol"));

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(RebacReasonCodes.NoRelationship, decision.Reasons[0].Code);

        var explanation = decision.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("openfga", explanation!.Engine);
        Assert.Equal(DeterminingRules.Relationship, explanation.DeterminingRule);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.RelationshipTuple, reference.Kind);
        Assert.Equal("user:carol#can_view@account:personal-carol", reference.Reference);
    }

    [Fact]
    public void Evaluate_ForwardsTheMappedCheck_ToTheSeam()
    {
        // The provider must forward exactly the mapped (user, relation, object) — the qualified
        // "user:<id>" / "account:<id>" and the action-derived relation — to the seam.
        var fake = FakeOpenFgaCheckClient.Allowing();

        Provider(fake).Evaluate(AccountRead("carol", "personal-carol"));

        Assert.Equal(1, fake.Calls);
        Assert.Equal("user:carol", fake.LastUser);
        Assert.Equal(RebacRelations.CanView, fake.LastRelation);
        Assert.Equal("account:personal-carol", fake.LastObject);
    }

    [Fact]
    public void Evaluate_TransactionCreateOnAccount_ChecksCanTransact_AndPermits()
    {
        // transaction.create on an account-shaped resource maps to the can_transact relation (not
        // can_view); on an allow the surfaced tuple names that relation.
        var fake = FakeOpenFgaCheckClient.Allowing();
        var request = new AccessRequest(
            new Subject("user", "carol", []),
            new ActionRequest(ActionNames.TransactionCreate),
            new Resource("account", Id: "personal-carol"),
            new EvaluationContext([]));

        var decision = Provider(fake).Evaluate(request);

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(RebacRelations.CanTransact, fake.LastRelation);
        var reference = Assert.Single(decision.Explanation!.PolicyReferences);
        Assert.Equal("user:carol#can_transact@account:personal-carol", reference.Reference);
    }

    [Fact]
    public void Evaluate_WhenSeamThrows_FailsClosed_Deny_EngineUnavailable()
    {
        // An unreachable/misbehaving engine surfaces as a thrown Check; Evaluate must DENY
        // (EngineUnavailable) rather than throw a raw 500 through /api/authz/evaluate.
        var decision = Provider(FakeOpenFgaCheckClient.Throwing(new InvalidOperationException("engine down")))
            .Evaluate(AccountRead("carol", "personal-carol"));

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(RebacReasonCodes.EngineUnavailable, decision.Reasons[0].Code);

        var explanation = decision.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("openfga", explanation!.Engine);
        Assert.Equal(DeterminingRules.EngineUnavailable, explanation.DeterminingRule);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.ReasonCode, reference.Kind);
        Assert.Equal(RebacReasonCodes.EngineUnavailable, reference.Reference);
    }

    [Fact]
    public void Evaluate_UnmappedAction_DeniesAtMapper_WithoutConsultingTheSeam()
    {
        // A fail-closed boundary deny (unknown action) must short-circuit BEFORE any Check: the seam
        // is never consulted, so a mapper-level deny can never be turned into an accidental engine
        // permit — proven by an always-ALLOW double that records zero calls.
        var fake = FakeOpenFgaCheckClient.Allowing();
        var request = new AccessRequest(
            new Subject("user", "carol", []),
            new ActionRequest("bank.account.delete"),
            new Resource("account", Id: "personal-carol"),
            new EvaluationContext([]));

        var decision = Provider(fake).Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.UnknownAction, decision.Reasons[0].Code);
        Assert.Equal(0, fake.Calls);
    }
}
