using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Pure mapping tests for the OpenFGA adapter: the AuthZEN AccessRequest -> OpenFGA Check
// translation and its fail-closed paths. No client or server — OpenFgaRequestMapper is a pure
// function, which is exactly why the provider factors it out.
public sealed class OpenFgaProviderMappingTests
{
    private static AccessRequest Request(string action, string subjectId, string resourceType, string? resourceId) =>
        new(
            new Subject("user", subjectId, []),
            new ActionRequest(action),
            new Resource(resourceType, Id: resourceId),
            new EvaluationContext([]));

    [Fact]
    public void AccountRead_MapsTo_CanView()
    {
        Assert.True(RebacActionMap.TryGetRelation(ActionNames.AccountRead, out var relation));
        Assert.Equal(RebacRelations.CanView, relation);
    }

    [Fact]
    public void TransactionCreate_MapsTo_CanTransact()
    {
        Assert.True(RebacActionMap.TryGetRelation(ActionNames.TransactionCreate, out var relation));
        Assert.Equal(RebacRelations.CanTransact, relation);
    }

    [Fact]
    public void UnknownAction_HasNoRelation()
    {
        Assert.False(RebacActionMap.TryGetRelation("bank.account.delete", out _));
    }

    [Fact]
    public void SupportedActions_AreExactlyTheMappedBankActions()
    {
        var actions = RebacActionMap.SupportedActions;

        Assert.Contains(ActionNames.AccountRead, actions);
        Assert.Contains(ActionNames.TransactionCreate, actions);
        Assert.Equal(2, actions.Count);
    }

    [Fact]
    public void TryMap_BuildsQualifiedUserRelationAndObject()
    {
        var request = Request(ActionNames.AccountRead, "rm-anne", "account", "acme-checking");

        var mapped = OpenFgaRequestMapper.TryMap(request, out var check, out _);

        Assert.True(mapped);
        Assert.Equal("user:rm-anne", check.User);
        Assert.Equal(RebacRelations.CanView, check.Relation);
        Assert.Equal("account:acme-checking", check.Object);
    }

    [Fact]
    public void TryMap_UnknownAction_FailsClosed_UnknownAction()
    {
        var request = Request("bank.account.delete", "rm-anne", "account", "acme-checking");

        var mapped = OpenFgaRequestMapper.TryMap(request, out _, out var denial);

        Assert.False(mapped);
        Assert.Equal(Decision.Deny, denial.Decision);
        Assert.Equal(ReasonCodes.UnknownAction, denial.Reasons[0].Code);
    }

    [Fact]
    public void TryMap_MissingResourceId_FailsClosed_MissingResourceId()
    {
        var request = Request(ActionNames.AccountRead, "rm-anne", "account", null);

        var mapped = OpenFgaRequestMapper.TryMap(request, out _, out var denial);

        Assert.False(mapped);
        Assert.Equal(Decision.Deny, denial.Decision);
        Assert.Equal(RebacReasonCodes.MissingResourceId, denial.Reasons[0].Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryMap_BlankResourceId_FailsClosed(string resourceId)
    {
        var request = Request(ActionNames.AccountRead, "rm-anne", "account", resourceId);

        Assert.False(OpenFgaRequestMapper.TryMap(request, out _, out var denial));
        Assert.Equal(RebacReasonCodes.MissingResourceId, denial.Reasons[0].Code);
    }

    [Fact]
    public void TryMap_NonAccountResource_FailsClosed_UnsupportedResourceType()
    {
        // The CS05 transaction shape (Resource.Type="transaction") has no queryable relation in the
        // ReBAC model, so it must deny cleanly rather than build a "transaction:..." object OpenFGA rejects.
        var request = Request(ActionNames.TransactionCreate, "rm-anne", "transaction", "txn-1");

        var mapped = OpenFgaRequestMapper.TryMap(request, out _, out var denial);

        Assert.False(mapped);
        Assert.Equal(Decision.Deny, denial.Decision);
        Assert.Equal(RebacReasonCodes.UnsupportedResourceType, denial.Reasons[0].Code);
    }

    [Fact]
    public void TryMap_TransactionCreate_OnAccountResource_MapsTo_CanTransact()
    {
        // An account-shaped transaction request maps to can_transact on the account object.
        var request = Request(ActionNames.TransactionCreate, "carol", "account", "personal-carol");

        var mapped = OpenFgaRequestMapper.TryMap(request, out var check, out _);

        Assert.True(mapped);
        Assert.Equal(RebacRelations.CanTransact, check.Relation);
        Assert.Equal("account:personal-carol", check.Object);
    }

    // --- CS16 explainability: relationship-path + boundary/fail-closed explanations -----------------

    // Builds the same fail-closed provider the registration suite uses: a blank ApiUrl means the first
    // Check throws, so Evaluate DENIES with an engine-unavailable explanation — never a live server.
    private static OpenFgaProvider FailClosedProvider() =>
        new(
            new OpenFgaRebacService(Options.Create(new OpenFgaOptions { ApiUrl = "" })),
            NullLogger<OpenFgaProvider>.Instance);

    [Fact]
    public void MappedRequest_YieldsTheRelationshipTupleReferenceTheProviderSurfaces()
    {
        // The relationship-path reference the provider attaches on a permit/deny is built purely from
        // the mapped check ("user:...#relation@account:..."), so it is verifiable offline from TryMap.
        var request = Request(ActionNames.AccountRead, "teller1", "account", "acme-checking");

        Assert.True(OpenFgaRequestMapper.TryMap(request, out var check, out _));

        var tuple = $"{check.User}#{check.Relation}@{check.Object}";
        Assert.Equal("user:teller1#can_view@account:acme-checking", tuple);
    }

    [Fact]
    public void Evaluate_FailClosed_CarriesEngineUnavailableExplanation()
    {
        var request = Request(ActionNames.AccountRead, "carol", "account", "personal-carol");

        var decision = FailClosedProvider().Evaluate(request);

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
    public void TryMap_UnknownAction_Denial_CarriesReasonCodeExplanation()
    {
        var request = Request("bank.account.delete", "rm-anne", "account", "acme-checking");

        Assert.False(OpenFgaRequestMapper.TryMap(request, out _, out var denial));

        var explanation = denial.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("openfga", explanation!.Engine);
        Assert.Equal(DeterminingRules.UnknownAction, explanation.DeterminingRule);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.ReasonCode, reference.Kind);
        Assert.Equal(ReasonCodes.UnknownAction, reference.Reference);
    }

    [Fact]
    public void TryMap_UnsupportedResourceType_Denial_CarriesReasonCodeExplanation()
    {
        var request = Request(ActionNames.TransactionCreate, "rm-anne", "transaction", "txn-1");

        Assert.False(OpenFgaRequestMapper.TryMap(request, out _, out var denial));

        var explanation = denial.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("openfga", explanation!.Engine);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.ReasonCode, reference.Kind);
        Assert.Equal(RebacReasonCodes.UnsupportedResourceType, reference.Reference);
    }

    [Fact]
    public void TryMap_MissingResourceId_Denial_CarriesReasonCodeExplanation()
    {
        var request = Request(ActionNames.AccountRead, "rm-anne", "account", null);

        Assert.False(OpenFgaRequestMapper.TryMap(request, out _, out var denial));

        var explanation = denial.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("openfga", explanation!.Engine);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.ReasonCode, reference.Kind);
        Assert.Equal(RebacReasonCodes.MissingResourceId, reference.Reference);
    }
}
