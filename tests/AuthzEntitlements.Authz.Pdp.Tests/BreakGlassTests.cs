using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// CS21 break-glass emergency elevation on the reference provider. Break-glass raises a base Deny for a
// MISSING CAPABILITY (MissingScope / RoleNotAuthorized) to a Permit carrying BreakGlassInvoked + the
// mandatory-post-review obligation ONLY when an active, matching grant is present; it NEVER overrides
// an integrity invariant (tenant, maker-checker/SoD, subject-is-maker, pending-status, unknown-action);
// and it fails closed on an expired/absent grant, a null injected clock, or a blank/mismatched grant.
// The human path (no BreakGlass in context) is byte-identical.
public sealed class BreakGlassTests
{
    private static readonly ReferenceDecisionProvider Provider = new();

    // The injected decision clock and a not-yet-expired expiry relative to it — the provider never
    // reads the wall clock, so expiry is a pure function of these values.
    private static readonly DateTimeOffset Now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Active = Now.AddHours(1);

    private static Subject Teller(string id = "user-teller1") =>
        new("user", id, [RoleNames.Teller], "CONTOSO");

    private static Subject Manager(string id = "user-manager1") =>
        new("user", id, [RoleNames.BranchManager], "CONTOSO");

    private static Resource Account(string tenant = "CONTOSO") => new("account", Tenant: tenant);

    private static Resource Transaction(decimal amount, string makerId, string? status = null, string tenant = "CONTOSO") =>
        new("transaction", Tenant: tenant, Amount: amount, MakerId: makerId, Status: status);

    private static BreakGlassGrant Grant(
        string subjectId, string action, DateTimeOffset? expiresAt = null) =>
        new("bg-1", subjectId, action, expiresAt ?? Active, "Incident #42 emergency access.");

    private static AccessDecision Evaluate(
        Subject subject, string action, Resource resource,
        BreakGlassGrant? grant, DateTimeOffset? now, params string[] scopes) =>
        Provider.Evaluate(new AccessRequest(
            subject, new ActionRequest(action), resource,
            new EvaluationContext(scopes, BreakGlass: grant, Now: now)));

    // ---- Break-glass elevates a MISSING-CAPABILITY deny ----

    [Fact]
    public void BreakGlass_Elevates_MissingScope_ToPermit()
    {
        var decision = Evaluate(
            Teller(), ActionNames.AccountRead, Account(),
            Grant("user-teller1", ActionNames.AccountRead), Now); // no read scope => base MissingScope

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.BreakGlassInvoked, decision.Reasons[0].Code);
    }

    [Fact]
    public void BreakGlass_Elevates_RoleNotAuthorized_ToPermit()
    {
        var decision = Evaluate(
            Teller(), ActionNames.AccountCreate, Account(),
            Grant("user-teller1", ActionNames.AccountCreate), Now); // teller lacks BranchManager

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.BreakGlassInvoked, decision.Reasons[0].Code);
    }

    [Fact]
    public void BreakGlass_ElevatedPermit_CarriesRequireBreakGlassReviewObligation()
    {
        var decision = Evaluate(
            Teller(), ActionNames.AccountRead, Account(),
            Grant("user-teller1", ActionNames.AccountRead), Now);

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Contains(decision.Obligations, o => o.Id == ObligationIds.RequireBreakGlassReview);
    }

    [Fact]
    public void BreakGlass_ElevatedPermit_CarriesBreakGlassExplanation()
    {
        var decision = Evaluate(
            Teller(), ActionNames.AccountRead, Account(),
            Grant("user-teller1", ActionNames.AccountRead), Now);

        Assert.NotNull(decision.Explanation);
        Assert.Equal("reference", decision.Explanation!.Engine);
        Assert.Equal(DeterminingRules.BreakGlass, decision.Explanation.DeterminingRule);
    }

    [Fact]
    public void BreakGlass_PermitReason_NamesGrantAndJustification()
    {
        var decision = Evaluate(
            Teller(), ActionNames.AccountRead, Account(),
            Grant("user-teller1", ActionNames.AccountRead), Now);

        Assert.Contains("bg-1", decision.Reasons[0].Message);
        Assert.Contains("Incident #42", decision.Reasons[0].Message);
    }

    // ---- Break-glass NEVER overrides an integrity invariant (the key fintech control) ----

    [Fact]
    public void BreakGlass_DoesNotOverride_TenantMismatch()
    {
        var decision = Evaluate(
            Teller(), ActionNames.AccountRead, Account("FABRIKAM"),
            Grant("user-teller1", ActionNames.AccountRead), Now, ScopeNames.Read);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.TenantMismatch, decision.Reasons[0].Code);
    }

    [Fact]
    public void BreakGlass_DoesNotOverride_MakerEqualsChecker()
    {
        var decision = Evaluate(
            Manager(), ActionNames.TransactionApprove,
            Transaction(15_000m, "user-manager1", "Pending"),
            Grant("user-manager1", ActionNames.TransactionApprove), Now, ScopeNames.ApprovalsWrite);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.MakerEqualsChecker, decision.Reasons[0].Code);
    }

    [Fact]
    public void BreakGlass_DoesNotOverride_SubjectNotMaker()
    {
        var decision = Evaluate(
            Teller(), ActionNames.TransactionCreate,
            Transaction(250m, "user-someone-else"),
            Grant("user-teller1", ActionNames.TransactionCreate), Now, ScopeNames.TransactionsWrite);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.SubjectNotMaker, decision.Reasons[0].Code);
    }

    [Fact]
    public void BreakGlass_DoesNotOverride_NotPending()
    {
        var decision = Evaluate(
            Manager(), ActionNames.TransactionApprove,
            Transaction(15_000m, "user-teller1", "Approved"),
            Grant("user-manager1", ActionNames.TransactionApprove), Now, ScopeNames.ApprovalsWrite);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.NotPending, decision.Reasons[0].Code);
    }

    [Fact]
    public void BreakGlass_DoesNotOverride_UnknownAction()
    {
        const string unknown = "bank.account.delete";
        var decision = Evaluate(
            Teller(), unknown, Account(),
            Grant("user-teller1", unknown), Now, ScopeNames.Read);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.UnknownAction, decision.Reasons[0].Code);
    }

    // ---- Break-glass must NOT elevate when a missing-capability denial MASKS an integrity violation ----
    // EvaluateCore returns only the FIRST failing reason and capability gates run BEFORE integrity gates,
    // so a request that lacks the scope/role AND violates an integrity invariant surfaces the elevatable
    // MissingScope/RoleNotAuthorized as its primary reason. Without the hard-invariant guard these would be
    // wrongly elevated (tenant isolation / SoD / maker bypass). Each asserts Deny AND that no elevation
    // happened (reason != BreakGlassInvoked, no review obligation).

    [Fact]
    public void BreakGlass_DoesNotElevate_MissingScope_MaskingTenantMismatch()
    {
        // No read scope (base MissingScope) AND cross-tenant read (integrity: tenant) + active grant.
        var decision = Evaluate(
            Teller(), ActionNames.AccountRead, Account("FABRIKAM"),
            Grant("user-teller1", ActionNames.AccountRead), Now); // no ScopeNames.Read

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.NotEqual(ReasonCodes.BreakGlassInvoked, decision.Reasons[0].Code);
        Assert.DoesNotContain(decision.Obligations, o => o.Id == ObligationIds.RequireBreakGlassReview);
    }

    [Fact]
    public void BreakGlass_DoesNotElevate_RoleNotAuthorized_MaskingTenantMismatch()
    {
        // Teller lacks BranchManager (base RoleNotAuthorized) AND cross-tenant create (integrity: tenant).
        var decision = Evaluate(
            Teller(), ActionNames.AccountCreate, Account("FABRIKAM"),
            Grant("user-teller1", ActionNames.AccountCreate), Now);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.NotEqual(ReasonCodes.BreakGlassInvoked, decision.Reasons[0].Code);
        Assert.DoesNotContain(decision.Obligations, o => o.Id == ObligationIds.RequireBreakGlassReview);
    }

    [Fact]
    public void BreakGlass_DoesNotElevate_MissingScope_MaskingSubjectNotMaker()
    {
        // No transactions.write scope (base MissingScope) AND subject != maker (integrity: subject-is-maker).
        var decision = Evaluate(
            Teller(), ActionNames.TransactionCreate,
            Transaction(250m, "user-someone-else"),
            Grant("user-teller1", ActionNames.TransactionCreate), Now); // no ScopeNames.TransactionsWrite

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.NotEqual(ReasonCodes.BreakGlassInvoked, decision.Reasons[0].Code);
        Assert.DoesNotContain(decision.Obligations, o => o.Id == ObligationIds.RequireBreakGlassReview);
    }

    [Fact]
    public void BreakGlass_DoesNotElevate_MissingScope_MaskingMakerEqualsChecker()
    {
        // No approvals.write scope (base MissingScope) AND self-approval (integrity: maker == checker).
        var decision = Evaluate(
            Manager(), ActionNames.TransactionApprove,
            Transaction(15_000m, "user-manager1", "Pending"),
            Grant("user-manager1", ActionNames.TransactionApprove), Now); // no ScopeNames.ApprovalsWrite

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.NotEqual(ReasonCodes.BreakGlassInvoked, decision.Reasons[0].Code);
        Assert.DoesNotContain(decision.Obligations, o => o.Id == ObligationIds.RequireBreakGlassReview);
    }

    [Fact]
    public void BreakGlass_DoesNotElevate_MissingScope_MaskingNotPending()
    {
        // No approvals.write scope (base MissingScope) AND target not pending (integrity: pending-status).
        var decision = Evaluate(
            Manager(), ActionNames.TransactionApprove,
            Transaction(15_000m, "user-teller1", "Approved"),
            Grant("user-manager1", ActionNames.TransactionApprove), Now); // no ScopeNames.ApprovalsWrite

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.NotEqual(ReasonCodes.BreakGlassInvoked, decision.Reasons[0].Code);
        Assert.DoesNotContain(decision.Obligations, o => o.Id == ObligationIds.RequireBreakGlassReview);
    }

    // ---- Positive controls: a PURE missing-capability denial (every hard invariant still holds) DOES
    // elevate — the guard must not over-block legitimate emergency access ----

    [Fact]
    public void BreakGlass_Elevates_MissingScope_WhenSubjectIsMaker_AndSameTenant()
    {
        // No transactions.write scope, but subject IS the maker and same-tenant: only capability is missing.
        var decision = Evaluate(
            Teller(), ActionNames.TransactionCreate,
            Transaction(250m, "user-teller1"),
            Grant("user-teller1", ActionNames.TransactionCreate), Now);

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.BreakGlassInvoked, decision.Reasons[0].Code);
        Assert.Contains(decision.Obligations, o => o.Id == ObligationIds.RequireBreakGlassReview);
    }

    [Fact]
    public void BreakGlass_Elevates_MissingScope_ForValidChecker_OnPendingSameTenant()
    {
        // No approvals.write scope, but a valid checker (different maker), pending, same-tenant: only
        // capability is missing => elevates.
        var decision = Evaluate(
            Manager(), ActionNames.TransactionApprove,
            Transaction(15_000m, "user-teller1", "Pending"),
            Grant("user-manager1", ActionNames.TransactionApprove), Now);

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.BreakGlassInvoked, decision.Reasons[0].Code);
        Assert.Contains(decision.Obligations, o => o.Id == ObligationIds.RequireBreakGlassReview);
    }

    // ---- Fail-closed: expiry boundary, null clock, blank/mismatched grant ----

    [Fact]
    public void BreakGlass_NotActive_WhenNowEqualsExpiresAt()
    {
        // Strict '<' expiry: at the exact expiry instant the grant is already expired => no elevation.
        var decision = Evaluate(
            Teller(), ActionNames.AccountRead, Account(),
            Grant("user-teller1", ActionNames.AccountRead, expiresAt: Now), Now);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.MissingScope, decision.Reasons[0].Code);
    }

    [Fact]
    public void BreakGlass_Active_WhenNowStrictlyBeforeExpiresAt()
    {
        var decision = Evaluate(
            Teller(), ActionNames.AccountRead, Account(),
            Grant("user-teller1", ActionNames.AccountRead, expiresAt: Now.AddTicks(1)), Now);

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.BreakGlassInvoked, decision.Reasons[0].Code);
    }

    [Fact]
    public void BreakGlass_NoElevation_WhenNowIsNull()
    {
        // The provider never reads the wall clock; without an injected clock a grant can never be active.
        var decision = Evaluate(
            Teller(), ActionNames.AccountRead, Account(),
            Grant("user-teller1", ActionNames.AccountRead), now: null);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.MissingScope, decision.Reasons[0].Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BreakGlass_NoElevation_WhenGrantSubjectBlank(string blankSubject)
    {
        var decision = Evaluate(
            Teller(), ActionNames.AccountRead, Account(),
            Grant(blankSubject, ActionNames.AccountRead), Now);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.MissingScope, decision.Reasons[0].Code);
    }

    [Fact]
    public void BreakGlass_NoElevation_WhenGrantSubjectMismatched()
    {
        var decision = Evaluate(
            Teller(), ActionNames.AccountRead, Account(),
            Grant("user-someone-else", ActionNames.AccountRead), Now);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.MissingScope, decision.Reasons[0].Code);
    }

    [Fact]
    public void BreakGlass_NoElevation_WhenGrantActionMismatched()
    {
        // Grant is for a different action than the request => not matching => no elevation.
        var decision = Evaluate(
            Teller(), ActionNames.AccountRead, Account(),
            Grant("user-teller1", ActionNames.AccountCreate), Now);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.MissingScope, decision.Reasons[0].Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BreakGlass_NoElevation_WhenGrantIdBlank(string blankId)
    {
        // Fail-closed: break-glass is an accountable control, so an unauditable grant (no correlation
        // id for the audit trail / mandatory review) must never elevate.
        var decision = Evaluate(
            Teller(), ActionNames.AccountRead, Account(),
            new BreakGlassGrant(blankId, "user-teller1", ActionNames.AccountRead, Active, "Incident #42."),
            Now);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.MissingScope, decision.Reasons[0].Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BreakGlass_NoElevation_WhenJustificationBlank(string blankJustification)
    {
        // Fail-closed: a break-glass grant with no recorded reason is not accountable, so it must not
        // elevate — the justification is surfaced on the permit reason and the audit trail.
        var decision = Evaluate(
            Teller(), ActionNames.AccountRead, Account(),
            new BreakGlassGrant("bg-1", "user-teller1", ActionNames.AccountRead, Active, blankJustification),
            Now);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.MissingScope, decision.Reasons[0].Code);
    }

    // ---- Human path: no BreakGlass in context is byte-identical to today's behaviour ----

    [Fact]
    public void HumanPath_MissingScopeDeny_NotElevated_WhenNoGrant()
    {
        var decision = Evaluate(
            Teller(), ActionNames.AccountRead, Account(),
            grant: null, now: Now); // grant absent, even though a clock is present

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.MissingScope, decision.Reasons[0].Code);
        Assert.Empty(decision.Obligations);
    }

    [Fact]
    public void HumanPath_NormalPermit_Unchanged_WithDefaultContext()
    {
        var decision = Provider.Evaluate(new AccessRequest(
            Teller(), new ActionRequest(ActionNames.AccountRead), Account(),
            new EvaluationContext([ScopeNames.Read])));

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.Permit, decision.Reasons[0].Code);
        Assert.Equal(DeterminingRules.AllRulesSatisfied, decision.Explanation!.DeterminingRule);
    }

    [Fact]
    public void ReasonForBreakGlassInvoked_MapsToBreakGlassRule()
    {
        Assert.Equal(
            DeterminingRules.BreakGlass,
            DecisionExplanations.RuleForReason(ReasonCodes.BreakGlassInvoked));
    }
}
