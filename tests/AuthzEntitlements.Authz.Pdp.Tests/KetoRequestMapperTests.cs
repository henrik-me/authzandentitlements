using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.Keto;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Pure mapping tests for the Keto adapter: the AuthZEN AccessRequest -> Keto permission-check
// translation and its fail-closed paths. No client or server — KetoRequestMapper is a pure function,
// which is exactly why the provider factors it out. Mirrors SpiceDbRequestMapperTests /
// OpenFgaProviderMappingTests so the three ReBAC adapters demonstrably map the same request to the same
// relationship question.
public sealed class KetoRequestMapperTests
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
        var request = Request(ActionNames.AccountRead, "rm-anne", "account", "acme-checking");

        var mapped = KetoRequestMapper.TryMap(request, out var check, out _);

        Assert.True(mapped);
        Assert.Equal("rm-anne", check.SubjectId);
        Assert.Equal(RebacRelations.CanView, check.Permission);
        Assert.Equal("acme-checking", check.AccountId);
    }

    [Fact]
    public void TransactionCreate_OnAccountResource_MapsTo_CanTransact()
    {
        var request = Request(ActionNames.TransactionCreate, "carol", "account", "personal-carol");

        var mapped = KetoRequestMapper.TryMap(request, out var check, out _);

        Assert.True(mapped);
        Assert.Equal(RebacRelations.CanTransact, check.Permission);
        Assert.Equal("personal-carol", check.AccountId);
    }

    [Fact]
    public void UnknownAction_FailsClosed_UnknownAction()
    {
        var request = Request("bank.account.delete", "rm-anne", "account", "acme-checking");

        var mapped = KetoRequestMapper.TryMap(request, out _, out var denial);

        Assert.False(mapped);
        Assert.Equal(Decision.Deny, denial.Decision);
        Assert.Equal(ReasonCodes.UnknownAction, denial.Reasons[0].Code);
    }

    [Fact]
    public void NonAccountResource_FailsClosed_UnsupportedResourceType()
    {
        // The CS05 transaction shape (Resource.Type="transaction") has no queryable permission in the
        // model, so it must deny cleanly rather than check a "transaction" namespace Keto does not define.
        var request = Request(ActionNames.TransactionCreate, "rm-anne", "transaction", "txn-1");

        var mapped = KetoRequestMapper.TryMap(request, out _, out var denial);

        Assert.False(mapped);
        Assert.Equal(Decision.Deny, denial.Decision);
        Assert.Equal(RebacReasonCodes.UnsupportedResourceType, denial.Reasons[0].Code);
    }

    [Fact]
    public void MissingResourceId_FailsClosed_MissingResourceId()
    {
        var request = Request(ActionNames.AccountRead, "rm-anne", "account", null);

        var mapped = KetoRequestMapper.TryMap(request, out _, out var denial);

        Assert.False(mapped);
        Assert.Equal(Decision.Deny, denial.Decision);
        Assert.Equal(RebacReasonCodes.MissingResourceId, denial.Reasons[0].Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankResourceId_FailsClosed(string resourceId)
    {
        var request = Request(ActionNames.AccountRead, "rm-anne", "account", resourceId);

        Assert.False(KetoRequestMapper.TryMap(request, out _, out var denial));
        Assert.Equal(RebacReasonCodes.MissingResourceId, denial.Reasons[0].Code);
    }

    // --- CS16 explainability: the boundary/fail-closed explanations carry the reason code ------------

    [Fact]
    public void UnknownAction_Denial_CarriesReasonCodeExplanation()
    {
        var request = Request("bank.account.delete", "rm-anne", "account", "acme-checking");

        Assert.False(KetoRequestMapper.TryMap(request, out _, out var denial));

        var explanation = denial.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("keto", explanation!.Engine);
        Assert.Equal(DeterminingRules.UnknownAction, explanation.DeterminingRule);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.ReasonCode, reference.Kind);
        Assert.Equal(ReasonCodes.UnknownAction, reference.Reference);
    }

    [Fact]
    public void UnsupportedResourceType_Denial_CarriesReasonCodeExplanation()
    {
        var request = Request(ActionNames.TransactionCreate, "rm-anne", "transaction", "txn-1");

        Assert.False(KetoRequestMapper.TryMap(request, out _, out var denial));

        var explanation = denial.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("keto", explanation!.Engine);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.ReasonCode, reference.Kind);
        Assert.Equal(RebacReasonCodes.UnsupportedResourceType, reference.Reference);
    }

    [Fact]
    public void MissingResourceId_Denial_CarriesReasonCodeExplanation()
    {
        var request = Request(ActionNames.AccountRead, "rm-anne", "account", null);

        Assert.False(KetoRequestMapper.TryMap(request, out _, out var denial));

        var explanation = denial.Explanation;
        Assert.NotNull(explanation);
        Assert.Equal("keto", explanation!.Engine);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.ReasonCode, reference.Kind);
        Assert.Equal(RebacReasonCodes.MissingResourceId, reference.Reference);
    }
}
