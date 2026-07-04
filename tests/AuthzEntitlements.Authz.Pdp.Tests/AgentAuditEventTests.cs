using AuthzEntitlements.Authz.Pdp.Audit;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// CS19 audit enrichment: the PdpDecisionService populates the new SubjectType / ActorId / ActorType
// fields from the request. A direct call records SubjectType from the Subject and leaves the actor
// fields null; an on-behalf-of (OBO) call records the human's SubjectType plus the delegate's id and
// type. Additive only — the existing decision/reason/explanation fields are asserted intact.
public sealed class AgentAuditEventTests
{
    private sealed class CapturingAuditSink : IPdpDecisionAuditSink
    {
        public List<PdpDecisionAuditEvent> Events { get; } = [];

        public void Record(PdpDecisionAuditEvent decisionEvent) => Events.Add(decisionEvent);
    }

    private static PdpDecisionService CreateService(IPdpDecisionAuditSink sink) =>
        new(
            new AuthorizationDecisionProviderFactory(
                [new ReferenceDecisionProvider()],
                Options.Create(new PdpOptions { Provider = "reference" })),
            sink);

    private static Actor Agent(params string[] scopes) => new("agent", "agent-copilot-1", scopes);

    [Fact]
    public void DirectRequest_RecordsUserSubjectType_AndNullActorFields()
    {
        var sink = new CapturingAuditSink();
        var service = CreateService(sink);
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountRead,
            new Resource("account", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        service.Evaluate(request);

        var evt = Assert.Single(sink.Events);
        Assert.Equal("user", evt.SubjectType);
        Assert.Null(evt.ActorId);
        Assert.Null(evt.ActorType);
    }

    [Fact]
    public void ServiceActingAsItself_RecordsServiceSubjectType_AndNullActorFields()
    {
        var sink = new CapturingAuditSink();
        var service = CreateService(sink);
        var request = PdpRequests.For(
            new Subject("service", "svc-1", [RoleNames.Teller], PdpRequests.Contoso),
            ActionNames.AccountRead,
            new Resource("account", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        service.Evaluate(request);

        var evt = Assert.Single(sink.Events);
        Assert.Equal("service", evt.SubjectType);
        Assert.Null(evt.ActorId);
        Assert.Null(evt.ActorType);
    }

    [Fact]
    public void OboPermit_RecordsSubjectTypeAndActorFields()
    {
        var sink = new CapturingAuditSink();
        var service = CreateService(sink);
        var request = PdpRequests.For(
            new Subject("user", "user-teller1", [RoleNames.Teller], PdpRequests.Contoso,
                Actor: Agent(AgentScopeNames.Read)),
            ActionNames.AccountRead,
            new Resource("account", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        var decision = service.Evaluate(request);

        Assert.Equal(Decision.Permit, decision.Decision);
        var evt = Assert.Single(sink.Events);
        Assert.Equal("user", evt.SubjectType);
        Assert.Equal("agent-copilot-1", evt.ActorId);
        Assert.Equal("agent", evt.ActorType);
        Assert.Equal("user-teller1", evt.SubjectId);
    }

    [Fact]
    public void OboDelegationDeny_StillRecordsActorFields()
    {
        var sink = new CapturingAuditSink();
        var service = CreateService(sink);
        var request = PdpRequests.For(
            new Subject("user", "user-teller1", [RoleNames.Teller], PdpRequests.Contoso,
                Actor: Agent(AgentScopeNames.Read)),
            ActionNames.TransactionCreate,
            new Resource("transaction", Tenant: PdpRequests.Contoso, Amount: 250m, MakerId: "user-teller1"),
            ScopeNames.TransactionsWrite);

        var decision = service.Evaluate(request);

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(ReasonCodes.DelegationScopeMissing, decision.Reasons[0].Code);
        var evt = Assert.Single(sink.Events);
        Assert.Equal(ReasonCodes.DelegationScopeMissing, evt.Reason);
        Assert.Equal(DeterminingRules.DelegationScope, evt.DeterminingRule);
        Assert.Equal("agent-copilot-1", evt.ActorId);
        Assert.Equal("agent", evt.ActorType);
        Assert.Equal("user", evt.SubjectType);
    }

    [Fact]
    public void ExistingAuditFields_RemainIntact_ForDirectRequest()
    {
        var sink = new CapturingAuditSink();
        var service = CreateService(sink);
        var request = PdpRequests.For(
            PdpRequests.User("user-teller1", PdpRequests.Contoso, RoleNames.Teller),
            ActionNames.AccountRead,
            new Resource("account", Id: "acct-1", Tenant: PdpRequests.Contoso),
            ScopeNames.Read);

        service.Evaluate(request);

        var evt = Assert.Single(sink.Events);
        Assert.Equal("reference", evt.Provider);
        Assert.Equal("user-teller1", evt.SubjectId);
        Assert.Equal(ActionNames.AccountRead, evt.Action);
        Assert.Equal("Permit", evt.Decision);
        Assert.Equal(ReasonCodes.Permit, evt.Reason);
        Assert.Equal(PdpRequests.Contoso, evt.Tenant);
        Assert.False(string.IsNullOrWhiteSpace(evt.Narrative));
    }
}
