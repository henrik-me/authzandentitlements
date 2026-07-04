using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Direct, per-rule-branch coverage of the reference engine. Each test builds exactly the
// AccessRequest that isolates one branch and asserts BOTH the decision and the primary
// reason code (plus the obligation where a permit carries one). Covers rule ORDERING too:
// a request that fails several checks returns the first-failing reason.
public sealed class ReferenceDecisionProviderTests
{
    private static readonly ReferenceDecisionProvider Provider = new();

    private static void AssertDeny(AccessRequest request, string expectedReason)
    {
        var decision = Provider.Evaluate(request);
        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(expectedReason, decision.Reasons[0].Code);
        Assert.Empty(decision.Obligations);
    }

    private static AccessDecision AssertPermit(AccessRequest request)
    {
        var decision = Provider.Evaluate(request);
        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.Permit, decision.Reasons[0].Code);
        return decision;
    }

    private static Resource Account(string? tenant) => new("account", Tenant: tenant);

    private static Resource Transaction(
        string? tenant, decimal amount, string makerId, string? status = null) =>
        new("transaction", Tenant: tenant, Amount: amount, MakerId: makerId, Status: status);

    // --- Read (bank.account.read): read scope + same tenant, no role gate ---

    [Fact]
    public void Read_SameTenant_WithReadScope_Permits()
    {
        var request = PdpRequests.For(
            PdpRequests.User("u1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountRead, Account(PdpRequests.Contoso), ScopeNames.Read);

        AssertPermit(request);
    }

    [Fact]
    public void Read_AnyAuthenticatedRole_SameTenant_Permits()
    {
        // No role gate on read: even the read-only Auditor role is permitted in-tenant.
        var request = PdpRequests.For(
            PdpRequests.User("u1", PdpRequests.Contoso, RoleNames.Auditor),
            ActionNames.AccountRead, Account(PdpRequests.Contoso), ScopeNames.Read);

        AssertPermit(request);
    }

    [Fact]
    public void Read_WithoutReadScope_DeniesMissingScope()
    {
        var request = PdpRequests.For(
            PdpRequests.User("u1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountRead, Account(PdpRequests.Contoso));

        AssertDeny(request, ReasonCodes.MissingScope);
    }

    [Fact]
    public void Read_WithWrongScopeOnly_DeniesMissingScope()
    {
        var request = PdpRequests.For(
            PdpRequests.User("u1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountRead, Account(PdpRequests.Contoso), ScopeNames.TransactionsWrite);

        AssertDeny(request, ReasonCodes.MissingScope);
    }

    [Fact]
    public void Read_CrossTenant_DeniesTenantMismatch()
    {
        var request = PdpRequests.For(
            PdpRequests.User("u1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountRead, Account(PdpRequests.Fabrikam), ScopeNames.Read);

        AssertDeny(request, ReasonCodes.TenantMismatch);
    }

    [Fact]
    public void Read_WithMissingSubjectTenant_FailsClosed_TenantMismatch()
    {
        var request = PdpRequests.For(
            PdpRequests.User("u1", tenant: null, RoleNames.Teller),
            ActionNames.AccountRead, Account(PdpRequests.Contoso), ScopeNames.Read);

        AssertDeny(request, ReasonCodes.TenantMismatch);
    }

    [Fact]
    public void Read_WithMissingResourceTenant_FailsClosed_TenantMismatch()
    {
        var request = PdpRequests.For(
            PdpRequests.User("u1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountRead, Account(tenant: null), ScopeNames.Read);

        AssertDeny(request, ReasonCodes.TenantMismatch);
    }

    [Fact]
    public void Read_MissingScopeCheckedBeforeTenant()
    {
        // No scope AND cross-tenant: scope is evaluated first, so MissingScope wins.
        var request = PdpRequests.For(
            PdpRequests.User("u1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountRead, Account(PdpRequests.Fabrikam));

        AssertDeny(request, ReasonCodes.MissingScope);
    }

    // --- Account create (bank.account.create): BranchManager + same tenant, no scope gate ---

    [Fact]
    public void AccountCreate_ByBranchManager_SameTenant_Permits()
    {
        var request = PdpRequests.For(
            PdpRequests.User("mgr", PdpRequests.Contoso, RoleNames.BranchManager),
            ActionNames.AccountCreate, Account(PdpRequests.Contoso), ScopeNames.Read);

        AssertPermit(request);
    }

    [Fact]
    public void AccountCreate_NeedsNoScope_Permits()
    {
        // Account create has no coarse-scope gate — a BranchManager in-tenant is permitted
        // even with an empty scope set.
        var request = PdpRequests.For(
            PdpRequests.User("mgr", PdpRequests.Contoso, RoleNames.BranchManager),
            ActionNames.AccountCreate, Account(PdpRequests.Contoso));

        AssertPermit(request);
    }

    [Fact]
    public void AccountCreate_ByNonBranchManager_DeniesRoleNotAuthorized()
    {
        var request = PdpRequests.For(
            PdpRequests.User("u1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountCreate, Account(PdpRequests.Contoso), ScopeNames.Read);

        AssertDeny(request, ReasonCodes.RoleNotAuthorized);
    }

    [Fact]
    public void AccountCreate_ByBranchManager_CrossTenant_DeniesTenantMismatch()
    {
        var request = PdpRequests.For(
            PdpRequests.User("mgr", PdpRequests.Contoso, RoleNames.BranchManager),
            ActionNames.AccountCreate, Account(PdpRequests.Fabrikam), ScopeNames.Read);

        AssertDeny(request, ReasonCodes.TenantMismatch);
    }

    [Fact]
    public void AccountCreate_RoleCheckedBeforeTenant()
    {
        // Non-BranchManager AND cross-tenant: role is evaluated first, so it wins.
        var request = PdpRequests.For(
            PdpRequests.User("u1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountCreate, Account(PdpRequests.Fabrikam), ScopeNames.Read);

        AssertDeny(request, ReasonCodes.RoleNotAuthorized);
    }

    // --- Transaction create (bank.transaction.create) ---

    [Theory]
    [InlineData(RoleNames.Teller)]
    [InlineData(RoleNames.BranchManager)]
    [InlineData(RoleNames.ComplianceOfficer)]
    public void TransactionCreate_ByMakerEligibleRole_AsSelf_Permits(string role)
    {
        var request = PdpRequests.For(
            PdpRequests.User("maker", PdpRequests.Contoso, role),
            ActionNames.TransactionCreate,
            Transaction(PdpRequests.Contoso, 250m, "maker"),
            ScopeNames.TransactionsWrite);

        AssertPermit(request);
    }

    [Fact]
    public void TransactionCreate_WithoutWriteScope_DeniesMissingScope()
    {
        var request = PdpRequests.For(
            PdpRequests.User("maker", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionCreate,
            Transaction(PdpRequests.Contoso, 250m, "maker"));

        AssertDeny(request, ReasonCodes.MissingScope);
    }

    [Fact]
    public void TransactionCreate_ByNonMakerEligibleRole_DeniesRoleNotAuthorized()
    {
        // Auditor holds the write scope and is the maker, but is not maker-eligible.
        var request = PdpRequests.For(
            PdpRequests.User("aud", PdpRequests.Contoso, RoleNames.Auditor),
            ActionNames.TransactionCreate,
            Transaction(PdpRequests.Contoso, 250m, "aud"),
            ScopeNames.TransactionsWrite);

        AssertDeny(request, ReasonCodes.RoleNotAuthorized);
    }

    [Fact]
    public void TransactionCreate_WhenSubjectIsNotMaker_DeniesSubjectNotMaker()
    {
        var request = PdpRequests.For(
            PdpRequests.User("maker", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionCreate,
            Transaction(PdpRequests.Contoso, 250m, "someone-else"),
            ScopeNames.TransactionsWrite);

        AssertDeny(request, ReasonCodes.SubjectNotMaker);
    }

    [Fact]
    public void TransactionCreate_CrossTenant_DeniesTenantMismatch()
    {
        var request = PdpRequests.For(
            PdpRequests.User("maker", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionCreate,
            Transaction(PdpRequests.Fabrikam, 250m, "maker"),
            ScopeNames.TransactionsWrite);

        AssertDeny(request, ReasonCodes.TenantMismatch);
    }

    [Fact]
    public void TransactionCreate_BelowThreshold_Permits_WithPostImmediately()
    {
        var request = PdpRequests.For(
            PdpRequests.User("maker", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionCreate,
            Transaction(PdpRequests.Contoso, 9_999.99m, "maker"),
            ScopeNames.TransactionsWrite);

        var decision = AssertPermit(request);
        var obligation = Assert.Single(decision.Obligations);
        Assert.Equal(ObligationIds.PostImmediately, obligation.Id);
    }

    [Fact]
    public void TransactionCreate_AtThresholdBoundary_Permits_WithRequireApproval()
    {
        // Exactly 10,000 is at/above the threshold (>=), so it obliges approval.
        var request = PdpRequests.For(
            PdpRequests.User("maker", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionCreate,
            Transaction(PdpRequests.Contoso, 10_000m, "maker"),
            ScopeNames.TransactionsWrite);

        var decision = AssertPermit(request);
        var obligation = Assert.Single(decision.Obligations);
        Assert.Equal(ObligationIds.RequireApproval, obligation.Id);
    }

    [Fact]
    public void TransactionCreate_AboveThreshold_Permits_WithRequireApproval()
    {
        var request = PdpRequests.For(
            PdpRequests.User("maker", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionCreate,
            Transaction(PdpRequests.Contoso, 25_000m, "maker"),
            ScopeNames.TransactionsWrite);

        var decision = AssertPermit(request);
        var obligation = Assert.Single(decision.Obligations);
        Assert.Equal(ObligationIds.RequireApproval, obligation.Id);
    }

    [Fact]
    public void TransactionCreate_WithNoAmount_TreatedBelowThreshold_PostImmediately()
    {
        // A missing amount defaults to 0, which is below the threshold.
        var request = PdpRequests.For(
            PdpRequests.User("maker", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionCreate,
            new Resource("transaction", Tenant: PdpRequests.Contoso, MakerId: "maker"),
            ScopeNames.TransactionsWrite);

        var decision = AssertPermit(request);
        var obligation = Assert.Single(decision.Obligations);
        Assert.Equal(ObligationIds.PostImmediately, obligation.Id);
    }

    // --- Approval decision (approve/reject share one rule path) ---

    [Theory]
    [InlineData(ActionNames.TransactionApprove)]
    [InlineData(ActionNames.TransactionReject)]
    public void Approval_HappyPath_Permits(string action)
    {
        var request = PdpRequests.For(
            PdpRequests.User("checker", PdpRequests.Contoso, RoleNames.BranchManager),
            action,
            Transaction(PdpRequests.Contoso, 15_000m, "maker", "Pending"),
            ScopeNames.ApprovalsWrite);

        AssertPermit(request);
    }

    [Theory]
    [InlineData(RoleNames.BranchManager)]
    [InlineData(RoleNames.ComplianceOfficer)]
    public void Approval_ByCheckerEligibleRole_Permits(string role)
    {
        var request = PdpRequests.For(
            PdpRequests.User("checker", PdpRequests.Contoso, role),
            ActionNames.TransactionApprove,
            Transaction(PdpRequests.Contoso, 15_000m, "maker", "Pending"),
            ScopeNames.ApprovalsWrite);

        AssertPermit(request);
    }

    [Theory]
    [InlineData(ActionNames.TransactionApprove)]
    [InlineData(ActionNames.TransactionReject)]
    public void Approval_WithoutApprovalsScope_DeniesMissingScope(string action)
    {
        var request = PdpRequests.For(
            PdpRequests.User("checker", PdpRequests.Contoso, RoleNames.BranchManager),
            action,
            Transaction(PdpRequests.Contoso, 15_000m, "maker", "Pending"));

        AssertDeny(request, ReasonCodes.MissingScope);
    }

    [Fact]
    public void Approval_ByTeller_DeniesRoleNotAuthorized()
    {
        var request = PdpRequests.For(
            PdpRequests.User("teller", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.TransactionApprove,
            Transaction(PdpRequests.Contoso, 15_000m, "maker", "Pending"),
            ScopeNames.ApprovalsWrite);

        AssertDeny(request, ReasonCodes.RoleNotAuthorized);
    }

    [Fact]
    public void Approval_ByAuditor_DeniesRoleNotAuthorized()
    {
        var request = PdpRequests.For(
            PdpRequests.User("aud", PdpRequests.Contoso, RoleNames.Auditor),
            ActionNames.TransactionApprove,
            Transaction(PdpRequests.Contoso, 15_000m, "maker", "Pending"),
            ScopeNames.ApprovalsWrite);

        AssertDeny(request, ReasonCodes.RoleNotAuthorized);
    }

    [Fact]
    public void Approval_CrossTenant_DeniesTenantMismatch()
    {
        var request = PdpRequests.For(
            PdpRequests.User("checker", PdpRequests.Contoso, RoleNames.BranchManager),
            ActionNames.TransactionApprove,
            Transaction(PdpRequests.Fabrikam, 15_000m, "maker", "Pending"),
            ScopeNames.ApprovalsWrite);

        AssertDeny(request, ReasonCodes.TenantMismatch);
    }

    [Fact]
    public void Approval_WhenCheckerIsMaker_DeniesMakerEqualsChecker()
    {
        // Segregation of duties: the checker may not be the maker.
        var request = PdpRequests.For(
            PdpRequests.User("mgr", PdpRequests.Contoso, RoleNames.BranchManager),
            ActionNames.TransactionApprove,
            Transaction(PdpRequests.Contoso, 15_000m, "mgr", "Pending"),
            ScopeNames.ApprovalsWrite);

        AssertDeny(request, ReasonCodes.MakerEqualsChecker);
    }

    [Fact]
    public void Approval_WhenTargetNotPending_DeniesNotPending()
    {
        var request = PdpRequests.For(
            PdpRequests.User("checker", PdpRequests.Contoso, RoleNames.BranchManager),
            ActionNames.TransactionApprove,
            Transaction(PdpRequests.Contoso, 15_000m, "maker", "Approved"),
            ScopeNames.ApprovalsWrite);

        AssertDeny(request, ReasonCodes.NotPending);
    }

    [Fact]
    public void Approval_PendingCheckedBeforeSoD()
    {
        // Checker == maker AND non-pending: pending is evaluated before SoD, mirroring
        // Bank.Api's Approval.Decide (already-decided rejected before the maker==checker check),
        // so the combined case denies NotPending — not MakerEqualsChecker.
        var request = PdpRequests.For(
            PdpRequests.User("mgr", PdpRequests.Contoso, RoleNames.BranchManager),
            ActionNames.TransactionApprove,
            Transaction(PdpRequests.Contoso, 15_000m, "mgr", "Approved"),
            ScopeNames.ApprovalsWrite);

        AssertDeny(request, ReasonCodes.NotPending);
    }

    // --- Fail-closed unknown action ---

    [Fact]
    public void UnknownAction_DeniesUnknownAction()
    {
        var request = PdpRequests.For(
            PdpRequests.User("u1", PdpRequests.Contoso, RoleNames.BranchManager),
            "bank.account.delete", Account(PdpRequests.Contoso), ScopeNames.Read);

        AssertDeny(request, ReasonCodes.UnknownAction);
    }

    [Fact]
    public void EmptyAction_DeniesUnknownAction()
    {
        var request = PdpRequests.For(
            PdpRequests.User("u1", PdpRequests.Contoso, RoleNames.BranchManager),
            string.Empty, Account(PdpRequests.Contoso), ScopeNames.Read);

        AssertDeny(request, ReasonCodes.UnknownAction);
    }

    [Fact]
    public void Provider_HasStableName()
    {
        Assert.Equal("reference", Provider.Name);
    }

    // --- CS16 explainability: every decision carries a "reference"-engine explanation ---

    [Fact]
    public void Permit_CarriesReferenceExplanation_AllRulesSatisfied()
    {
        var request = PdpRequests.For(
            PdpRequests.User("u1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountRead, Account(PdpRequests.Contoso), ScopeNames.Read);

        var decision = Provider.Evaluate(request);

        Assert.Equal(Decision.Permit, decision.Decision);
        var explanation = Assert.IsType<DecisionExplanation>(decision.Explanation);
        Assert.Equal("reference", explanation.Engine);
        Assert.Equal(DeterminingRules.AllRulesSatisfied, explanation.DeterminingRule);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.Rule, reference.Kind);
        Assert.Equal(DeterminingRules.AllRulesSatisfied, reference.Reference);
    }

    [Fact]
    public void ScopeDeny_CarriesReferenceExplanation_ScopeRule()
    {
        var request = PdpRequests.For(
            PdpRequests.User("u1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountRead, Account(PdpRequests.Contoso));

        var decision = Provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.MissingScope, decision.Reasons[0].Code);
        var explanation = Assert.IsType<DecisionExplanation>(decision.Explanation);
        Assert.Equal("reference", explanation.Engine);
        Assert.Equal(DeterminingRules.Scope, explanation.DeterminingRule);
        Assert.Equal(decision.Reasons[0].Message, explanation.Narrative);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.Rule, reference.Kind);
        Assert.Equal(DeterminingRules.Scope, reference.Reference);
    }

    [Fact]
    public void RoleDeny_CarriesReferenceExplanation_RuleKind()
    {
        // The reference owns its role set, so a role denial surfaces a normalized "rule"
        // reference (kind "rule") rather than an engine-native casbin-rule/aspnet-requirement.
        var request = PdpRequests.For(
            PdpRequests.User("u1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountCreate, Account(PdpRequests.Contoso), ScopeNames.Read);

        var decision = Provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.RoleNotAuthorized, decision.Reasons[0].Code);
        var explanation = Assert.IsType<DecisionExplanation>(decision.Explanation);
        Assert.Equal("reference", explanation.Engine);
        Assert.Equal(DeterminingRules.Role, explanation.DeterminingRule);
        var reference = Assert.Single(explanation.PolicyReferences);
        Assert.Equal(PolicyReferenceKinds.Rule, reference.Kind);
        Assert.Equal(DeterminingRules.Role, reference.Reference);
    }

    [Fact]
    public void UnknownAction_CarriesReferenceExplanation_UnknownActionRule()
    {
        var request = PdpRequests.For(
            PdpRequests.User("u1", PdpRequests.Contoso, RoleNames.BranchManager),
            "bank.account.delete", Account(PdpRequests.Contoso), ScopeNames.Read);

        var decision = Provider.Evaluate(request);

        var explanation = Assert.IsType<DecisionExplanation>(decision.Explanation);
        Assert.Equal("reference", explanation.Engine);
        Assert.Equal(DeterminingRules.UnknownAction, explanation.DeterminingRule);
        Assert.Equal(DeterminingRules.UnknownAction, Assert.Single(explanation.PolicyReferences).Reference);
    }
}
