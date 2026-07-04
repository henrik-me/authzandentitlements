using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// CS21 manager->delegate delegation-grant enforcement on the reference provider. On an on-behalf-of
// (OBO) call whose base is permitted and whose Actor holds the delegated scope, a delegation grant in
// context must ALSO be active and match this manager (Subject) -> delegate (Actor), else the request
// denies DelegationNotActive. When no delegation grant is in context the behaviour is EXACTLY the CS19
// agent-OBO path (DelegationScopeMissing / permit). A base Deny still passes through unchanged.
public sealed class DelegationGrantTests
{
    private static readonly ReferenceDecisionProvider Provider = new();

    private static readonly DateTimeOffset Now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Active = Now.AddHours(1);
    private static readonly DateTimeOffset Expired = Now.AddHours(-1);

    private const string Manager1 = "user-manager1";
    private const string Delegate1 = "user-delegate1";

    private static Actor Delegate(params string[] scopes) => new("user", Delegate1, scopes);

    private static Actor Agent(params string[] scopes) => new("agent", "agent-1", scopes);

    private static Subject DelegatedManager(Actor actor) =>
        new("user", Manager1, [RoleNames.BranchManager], "CONTOSO", Actor: actor);

    private static Subject OboUser(Actor actor) =>
        new("user", "user-teller1", [RoleNames.Teller], "CONTOSO", Actor: actor);

    private static Resource Account(string tenant = "CONTOSO") => new("account", Tenant: tenant);

    private static DelegationGrant Grant(
        string managerId, string delegateId, DateTimeOffset? expiresAt = null) =>
        new("del-1", managerId, delegateId, expiresAt ?? Active);

    private static AccessDecision Evaluate(
        Subject subject, string action, Resource resource,
        DelegationGrant? delegation, DateTimeOffset? now, params string[] scopes) =>
        Provider.Evaluate(new AccessRequest(
            subject, new ActionRequest(action), resource,
            new EvaluationContext(scopes, Delegation: delegation, Now: now)));

    // ---- Active grant: the delegate acts for the manager ----

    [Fact]
    public void Delegation_ActiveGrant_AndDelegateHasScope_Permits()
    {
        var decision = Evaluate(
            DelegatedManager(Delegate(AgentScopeNames.Read)),
            ActionNames.AccountRead, Account(),
            Grant(Manager1, Delegate1), Now, ScopeNames.Read);

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.Permit, decision.Reasons[0].Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Delegation_Denies_WhenGrantIdBlank(string blankId)
    {
        // Fail-closed: a delegation grant with no correlation id is unauditable (DelegationId could not
        // be tied back), so it must not authorize even with a matching manager/delegate and the scope.
        var decision = Evaluate(
            DelegatedManager(Delegate(AgentScopeNames.Read)),
            ActionNames.AccountRead, Account(),
            new DelegationGrant(blankId, Manager1, Delegate1, Active), Now, ScopeNames.Read);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.DelegationNotActive, decision.Reasons[0].Code);
    }

    // ---- Fail-closed: DelegationNotActive on expiry / mismatch / null clock ----

    [Fact]
    public void Delegation_ExpiredGrant_Denies()
    {
        var decision = Evaluate(
            DelegatedManager(Delegate(AgentScopeNames.Read)),
            ActionNames.AccountRead, Account(),
            Grant(Manager1, Delegate1, Expired), Now, ScopeNames.Read);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.DelegationNotActive, decision.Reasons[0].Code);
    }

    [Fact]
    public void Delegation_MismatchedManager_Denies()
    {
        var decision = Evaluate(
            DelegatedManager(Delegate(AgentScopeNames.Read)),
            ActionNames.AccountRead, Account(),
            Grant("user-other-manager", Delegate1), Now, ScopeNames.Read);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.DelegationNotActive, decision.Reasons[0].Code);
    }

    [Fact]
    public void Delegation_MismatchedDelegate_Denies()
    {
        var decision = Evaluate(
            DelegatedManager(Delegate(AgentScopeNames.Read)),
            ActionNames.AccountRead, Account(),
            Grant(Manager1, "user-other-delegate"), Now, ScopeNames.Read);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.DelegationNotActive, decision.Reasons[0].Code);
    }

    [Fact]
    public void Delegation_NullNow_Denies()
    {
        var decision = Evaluate(
            DelegatedManager(Delegate(AgentScopeNames.Read)),
            ActionNames.AccountRead, Account(),
            Grant(Manager1, Delegate1), now: null, ScopeNames.Read);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.DelegationNotActive, decision.Reasons[0].Code);
    }

    [Fact]
    public void Delegation_Deny_CarriesDelegationExplanation()
    {
        var decision = Evaluate(
            DelegatedManager(Delegate(AgentScopeNames.Read)),
            ActionNames.AccountRead, Account(),
            Grant(Manager1, Delegate1, Expired), Now, ScopeNames.Read);

        Assert.NotNull(decision.Explanation);
        Assert.Equal("reference", decision.Explanation!.Engine);
        Assert.Equal(DeterminingRules.Delegation, decision.Explanation.DeterminingRule);
    }

    [Fact]
    public void Delegation_DenyReason_NamesDelegateManagerAndGrant()
    {
        var decision = Evaluate(
            DelegatedManager(Delegate(AgentScopeNames.Read)),
            ActionNames.AccountRead, Account(),
            Grant(Manager1, Delegate1, Expired), Now, ScopeNames.Read);

        Assert.Contains(Delegate1, decision.Reasons[0].Message);
        Assert.Contains(Manager1, decision.Reasons[0].Message);
        Assert.Contains("del-1", decision.Reasons[0].Message);
    }

    // ---- A base Deny still passes through unchanged (delegation grant never consulted) ----

    [Fact]
    public void Delegation_BaseDeny_PassesThrough_EvenWithActiveGrant()
    {
        var decision = Evaluate(
            DelegatedManager(Delegate(AgentScopeNames.Read)),
            ActionNames.AccountRead, Account("FABRIKAM"),
            Grant(Manager1, Delegate1), Now, ScopeNames.Read);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.TenantMismatch, decision.Reasons[0].Code);
    }

    // ---- The OBO scope check precedes the delegation-grant check ----

    [Fact]
    public void Delegation_MissingDelegatedScope_TakesPrecedence_OverInactiveGrant()
    {
        var decision = Evaluate(
            DelegatedManager(Delegate()), // delegate holds no delegated scope
            ActionNames.AccountRead, Account(),
            Grant(Manager1, Delegate1, Expired), Now, ScopeNames.Read);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.DelegationScopeMissing, decision.Reasons[0].Code);
    }

    // ---- CS19 agent OBO is unchanged when no Delegation grant is in context ----

    [Fact]
    public void Cs19AgentObo_NoDelegationInContext_Permits_ExactlyAsBefore()
    {
        var decision = Evaluate(
            OboUser(Agent(AgentScopeNames.Read)),
            ActionNames.AccountRead, Account(),
            delegation: null, now: null, ScopeNames.Read);

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.Permit, decision.Reasons[0].Code);
    }

    [Fact]
    public void Cs19AgentObo_NoDelegationInContext_DelegationScopeMissing_ExactlyAsBefore()
    {
        var decision = Evaluate(
            OboUser(Agent()), // agent holds no delegated scope
            ActionNames.AccountRead, Account(),
            delegation: null, now: null, ScopeNames.Read);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.DelegationScopeMissing, decision.Reasons[0].Code);
    }

    [Fact]
    public void ReasonForDelegationNotActive_MapsToDelegationRule()
    {
        Assert.Equal(
            DeterminingRules.Delegation,
            DecisionExplanations.RuleForReason(ReasonCodes.DelegationNotActive));
    }
}
