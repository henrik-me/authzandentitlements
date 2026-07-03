using AuthzEntitlements.Authz.Pdp.Contracts;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Guards the WIRE CONTRACT. Reason codes, action/scope/role names, and obligation ids are
// stable identifiers other layers and CS06-CS09 adapters key on: a silent rename is a
// breaking change, so these exact-string assertions turn any rename into a failing test.
public sealed class ContractVocabularyTests
{
    [Fact]
    public void ReasonCodes_HaveStableWireValues()
    {
        Assert.Equal("Permit", ReasonCodes.Permit);
        Assert.Equal("MissingScope", ReasonCodes.MissingScope);
        Assert.Equal("TenantMismatch", ReasonCodes.TenantMismatch);
        Assert.Equal("RoleNotAuthorized", ReasonCodes.RoleNotAuthorized);
        Assert.Equal("SubjectNotMaker", ReasonCodes.SubjectNotMaker);
        Assert.Equal("MakerEqualsChecker", ReasonCodes.MakerEqualsChecker);
        Assert.Equal("NotPending", ReasonCodes.NotPending);
        Assert.Equal("BranchNotInTenant", ReasonCodes.BranchNotInTenant);
        Assert.Equal("UnknownAction", ReasonCodes.UnknownAction);
    }

    [Fact]
    public void ActionNames_HaveStableWireValues()
    {
        Assert.Equal("bank.account.read", ActionNames.AccountRead);
        Assert.Equal("bank.account.create", ActionNames.AccountCreate);
        Assert.Equal("bank.transaction.create", ActionNames.TransactionCreate);
        Assert.Equal("bank.transaction.approve", ActionNames.TransactionApprove);
        Assert.Equal("bank.transaction.reject", ActionNames.TransactionReject);
    }

    [Fact]
    public void ScopeNames_HaveStableWireValues()
    {
        Assert.Equal("bank.read", ScopeNames.Read);
        Assert.Equal("bank.transactions.write", ScopeNames.TransactionsWrite);
        Assert.Equal("bank.approvals.write", ScopeNames.ApprovalsWrite);
    }

    [Fact]
    public void RoleNames_HaveStableValues()
    {
        Assert.Equal("Teller", RoleNames.Teller);
        Assert.Equal("BranchManager", RoleNames.BranchManager);
        Assert.Equal("ComplianceOfficer", RoleNames.ComplianceOfficer);
        Assert.Equal("Auditor", RoleNames.Auditor);
    }

    [Fact]
    public void ObligationIds_HaveStableWireValues()
    {
        Assert.Equal("require_approval", ObligationIds.RequireApproval);
        Assert.Equal("post_immediately", ObligationIds.PostImmediately);
    }

    [Fact]
    public void Decision_DenyIsZero_ForFailClosedDefault()
    {
        Assert.Equal(0, (int)Decision.Deny);
        Assert.Equal(1, (int)Decision.Permit);
        Assert.Equal(Decision.Deny, default(Decision));
    }

    [Fact]
    public void AccessDecision_Deny_HasNoObligations_AndCarriesReason()
    {
        var reason = new Reason(ReasonCodes.TenantMismatch, "cross-tenant");

        var decision = AccessDecision.Deny(reason);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Empty(decision.Obligations);
        Assert.Equal(reason, Assert.Single(decision.Reasons));
    }

    [Fact]
    public void AccessDecision_Permit_CarriesReasonAndObligation()
    {
        var reason = new Reason(ReasonCodes.Permit, "ok");
        var obligation = new Obligation(ObligationIds.RequireApproval);

        var decision = AccessDecision.Permit(reason, obligation);

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(reason, Assert.Single(decision.Reasons));
        Assert.Equal(obligation, Assert.Single(decision.Obligations));
    }

    [Fact]
    public void AccessDecision_Permit_WithNoObligations_IsEmpty()
    {
        var decision = AccessDecision.Permit(new Reason(ReasonCodes.Permit, "ok"));

        Assert.Empty(decision.Obligations);
    }

    [Fact]
    public void Obligation_DefaultsToNullProperties()
    {
        var obligation = new Obligation(ObligationIds.PostImmediately);

        Assert.Null(obligation.Properties);
    }

    [Fact]
    public void Obligation_CanCarryStructuredProperties()
    {
        var properties = new Dictionary<string, string> { ["threshold"] = "10000" };

        var obligation = new Obligation(ObligationIds.RequireApproval, properties);

        var carried = obligation.Properties;
        Assert.NotNull(carried);
        Assert.Equal("10000", Assert.Contains("threshold", carried));
    }
}
