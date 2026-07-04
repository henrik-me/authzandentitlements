using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
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
}
