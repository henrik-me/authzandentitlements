using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// CS19 constrained-delegation semantics on the reference provider: the direct/human path is
// byte-identical when Subject.Actor is null; an on-behalf-of (OBO) request is authorized only at
// the INTERSECTION of the human's rights and the agent's delegated scopes; a base human Deny
// passes through with its reason preserved; and the new delegation deny carries a DecisionExplanation
// whose determining rule is "delegation-scope". Also pins the AgentScopeNames.RequiredFor map.
public sealed class AgentDelegationTests
{
    private static readonly ReferenceDecisionProvider Provider = new();

    private static Actor Agent(params string[] scopes) => new("agent", "agent-1", scopes);

    // ---- Direct/human path is unchanged when Actor == null ----

    public static IEnumerable<object[]> HumanScenarios() =>
        AuthzEntitlements.Authz.Pdp.Catalog.FintechScenarioCatalog.Scenarios
            .Select(s => new object[] { s.Id });

    [Theory]
    [MemberData(nameof(HumanScenarios))]
    public void DirectPath_IsByteIdentical_WhenActorIsNull(string scenarioId)
    {
        var scenario = AuthzEntitlements.Authz.Pdp.Catalog.FintechScenarioCatalog.Scenarios
            .Single(s => s.Id == scenarioId);

        // The catalog subjects already carry no Actor; asserting equality against a freshly
        // constructed Actor-null subject proves the wrapper leaves the human path untouched.
        var withNullActor = scenario.Request with
        {
            Subject = scenario.Request.Subject with { Actor = null },
        };

        var baseline = Provider.Evaluate(scenario.Request);
        var actual = Provider.Evaluate(withNullActor);

        Assert.Equal(baseline.Decision, actual.Decision);
        Assert.Equal(baseline.Reasons[0].Code, actual.Reasons[0].Code);
        Assert.Equal(baseline.Reasons[0].Message, actual.Reasons[0].Message);
        Assert.Equal(
            baseline.Obligations.Select(o => o.Id),
            actual.Obligations.Select(o => o.Id));
        Assert.Equal(baseline.Explanation!.DeterminingRule, actual.Explanation!.DeterminingRule);
        Assert.Equal(scenario.Expected, actual.Decision);
        Assert.Equal(scenario.ExpectedReasonCode, actual.Reasons[0].Code);
    }

    [Fact]
    public void ServiceActingAsItself_WithNullActor_TakesDirectPath()
    {
        var request = PdpRequests.For(
            new Subject("service", "svc-1", [RoleNames.Teller], PdpRequests.Contoso),
            ActionNames.AccountRead,
            new Resource("account", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        var decision = Provider.Evaluate(request);

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.Permit, decision.Reasons[0].Code);
    }

    // ---- OBO permit: human-permit AND agent holds the delegated scope ----

    [Fact]
    public void Obo_Permit_WhenHumanPermitted_AndAgentHasDelegatedScope()
    {
        var request = PdpRequests.For(
            new Subject("user", "user-teller1", [RoleNames.Teller], PdpRequests.Contoso,
                Actor: Agent(AgentScopeNames.Read)),
            ActionNames.AccountRead,
            new Resource("account", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        var decision = Provider.Evaluate(request);

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.Permit, decision.Reasons[0].Code);
    }

    [Fact]
    public void Obo_Permit_LargeTransaction_PreservesObligation()
    {
        var request = PdpRequests.For(
            new Subject("user", "user-teller1", [RoleNames.Teller], PdpRequests.Contoso,
                Actor: Agent(AgentScopeNames.TransactionsWrite)),
            ActionNames.TransactionCreate,
            new Resource("transaction", Tenant: PdpRequests.Contoso, Amount: 15_000m, MakerId: "user-teller1"),
            ScopeNames.TransactionsWrite);

        var decision = Provider.Evaluate(request);

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Contains(decision.Obligations, o => o.Id == ObligationIds.RequireApproval);
    }

    // ---- OBO deny: human-permit but agent MISSING the delegated scope ----

    [Fact]
    public void Obo_Deny_DelegationScopeMissing_WhenAgentLacksScope()
    {
        var request = PdpRequests.For(
            new Subject("user", "user-teller1", [RoleNames.Teller], PdpRequests.Contoso,
                Actor: Agent(AgentScopeNames.Read)),
            ActionNames.TransactionCreate,
            new Resource("transaction", Tenant: PdpRequests.Contoso, Amount: 250m, MakerId: "user-teller1"),
            ScopeNames.TransactionsWrite);

        var decision = Provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.DelegationScopeMissing, decision.Reasons[0].Code);
    }

    [Fact]
    public void Obo_Deny_WhenAgentScopesEmpty_FailsClosed()
    {
        var request = PdpRequests.For(
            new Subject("user", "user-teller1", [RoleNames.Teller], PdpRequests.Contoso,
                Actor: Agent()),
            ActionNames.AccountRead,
            new Resource("account", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        var decision = Provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.DelegationScopeMissing, decision.Reasons[0].Code);
    }

    [Fact]
    public void Obo_Deny_ForActionWithNoDelegatedScopeMapping_FailsClosed()
    {
        // The governance SoD action maps to the read delegated scope; an agent holding only the
        // write scope must be denied. (There is no bank action mapped to null that also base-permits;
        // this proves an agent without the required mapped scope denies with the delegation code.)
        var request = PdpRequests.For(
            new Subject("user", "user-1", [RoleNames.Teller], PdpRequests.Contoso,
                Actor: Agent(AgentScopeNames.TransactionsWrite)),
            ActionNames.GovernanceAccessRequest,
            new Resource("governance"),
            ScopeNames.Read);

        var decision = Provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.DelegationScopeMissing, decision.Reasons[0].Code);
    }

    // ---- OBO passthrough: base human Deny is returned unchanged (agent cannot exceed the human) ----

    [Fact]
    public void Obo_Deny_PassesThroughHumanReason_OnCrossTenantRead()
    {
        var request = PdpRequests.For(
            new Subject("user", "user-teller1", [RoleNames.Teller], PdpRequests.Contoso,
                Actor: Agent(AgentScopeNames.Read)),
            ActionNames.AccountRead,
            new Resource("account", Tenant: PdpRequests.Fabrikam),
            ScopeNames.Read);

        var decision = Provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.TenantMismatch, decision.Reasons[0].Code);
    }

    [Fact]
    public void Obo_Deny_PassesThroughHumanReason_OnRoleIneligible_EvenWithDelegatedScope()
    {
        var request = PdpRequests.For(
            new Subject("user", "user-teller1", [RoleNames.Teller], PdpRequests.Contoso,
                Actor: Agent(AgentScopeNames.ApprovalsWrite)),
            ActionNames.TransactionApprove,
            new Resource("transaction", Tenant: PdpRequests.Contoso, Amount: 15_000m,
                MakerId: "user-manager1", Status: "Pending"),
            ScopeNames.ApprovalsWrite);

        var decision = Provider.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.RoleNotAuthorized, decision.Reasons[0].Code);
    }

    // ---- The delegation deny carries a DecisionExplanation with the delegation-scope rule ----

    [Fact]
    public void Obo_DelegationDeny_CarriesDelegationScopeExplanation()
    {
        var request = PdpRequests.For(
            new Subject("user", "user-teller1", [RoleNames.Teller], PdpRequests.Contoso,
                Actor: Agent(AgentScopeNames.Read)),
            ActionNames.TransactionCreate,
            new Resource("transaction", Tenant: PdpRequests.Contoso, Amount: 250m, MakerId: "user-teller1"),
            ScopeNames.TransactionsWrite);

        var decision = Provider.Evaluate(request);

        Assert.NotNull(decision.Explanation);
        Assert.Equal("reference", decision.Explanation!.Engine);
        Assert.Equal(DeterminingRules.DelegationScope, decision.Explanation.DeterminingRule);
        Assert.Contains(
            decision.Explanation.PolicyReferences,
            r => r.Reference == DeterminingRules.DelegationScope);
    }

    // ---- AgentScopeNames.RequiredFor mapping ----

    [Theory]
    [InlineData(ActionNames.AccountRead, AgentScopeNames.Read)]
    [InlineData(ActionNames.AccountCreate, AgentScopeNames.TransactionsWrite)]
    [InlineData(ActionNames.TransactionCreate, AgentScopeNames.TransactionsWrite)]
    [InlineData(ActionNames.TransactionApprove, AgentScopeNames.ApprovalsWrite)]
    [InlineData(ActionNames.TransactionReject, AgentScopeNames.ApprovalsWrite)]
    [InlineData(ActionNames.GovernanceAccessRequest, AgentScopeNames.Read)]
    public void RequiredFor_MapsKnownActions(string action, string expectedScope)
    {
        Assert.Equal(expectedScope, AgentScopeNames.RequiredFor(action));
    }

    [Theory]
    [InlineData("bank.account.delete")]
    [InlineData("")]
    [InlineData("totally.unknown")]
    public void RequiredFor_UnknownAction_ReturnsNull_FailsClosed(string action)
    {
        Assert.Null(AgentScopeNames.RequiredFor(action));
    }

    [Fact]
    public void ReasonForDelegationScopeMissing_MapsToDelegationScopeRule()
    {
        Assert.Equal(
            DeterminingRules.DelegationScope,
            DecisionExplanations.RuleForReason(ReasonCodes.DelegationScopeMissing));
    }
}
