using AuthzEntitlements.Bank.Web.Clients;
using AuthzEntitlements.Bank.Web.ViewModels;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

// Offline unit tests for the CS21 "Delegation" (manager -> delegate) page model. No server, Docker, or
// Keycloak required — they exercise the demo scenario, the manager-direct-vs-delegate request authoring
// (which must differ ONLY by the Actor + the delegation grant + the injected clock), the issued-grant
// -> PDP-context mapping (which must be 1:1), and the fail-closed decision mapping (a null decision →
// Deny, a DelegationNotActive deny surfaced correctly).
public class DelegationModelTests
{
    private static DelegationGrantResponse Grant(
        string managerId, string delegateId, string[] scopes, DateTimeOffset expiresAt) =>
        new(
            Guid.NewGuid(),
            managerId,
            delegateId,
            "CONTOSO",
            scopes,
            GrantedAt: expiresAt.AddMinutes(-60),
            ExpiresAt: expiresAt,
            RevokedAt: null,
            RevokedBy: null,
            Active: true,
            Status: "active");

    [Fact]
    public void Scenarios_are_non_empty_and_have_a_delegate_scope()
    {
        Assert.NotEmpty(DelegationModel.Scenarios);
        Assert.All(DelegationModel.Scenarios, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Title));
            Assert.False(string.IsNullOrWhiteSpace(s.Explanation));
            Assert.NotEmpty(s.Delegate.DelegatedScopes);
            Assert.True(s.DurationMinutes > 0);
        });
    }

    [Fact]
    public void BuildManagerDirectRequest_has_null_actor_and_no_delegation()
    {
        foreach (var scenario in DelegationModel.Scenarios)
        {
            var request = DelegationModel.BuildManagerDirectRequest(scenario);

            Assert.Null(request.Subject.Actor);
            Assert.Null(request.Context.Delegation);
            Assert.Null(request.Context.Now);
            Assert.Equal(scenario.Manager.Id, request.Subject.Id);
            Assert.Equal(scenario.ManagerScopes, request.Context.Scopes);
            Assert.Equal(scenario.Action, request.Action.Name);
        }
    }

    [Fact]
    public void BuildDelegateRequest_sets_actor_delegation_and_now()
    {
        var scenario = DelegationModel.Scenarios[0];
        var now = new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        var grant = Grant(
            scenario.Manager.Id, scenario.Delegate.Id,
            scenario.Delegate.DelegatedScopes.ToArray(), now.AddHours(1));

        var request = DelegationModel.BuildDelegateRequest(scenario, grant, now);

        Assert.NotNull(request.Subject.Actor);
        Assert.Equal("user", request.Subject.Actor!.Type);
        Assert.Equal(scenario.Delegate.Id, request.Subject.Actor.Id);
        Assert.Equal(scenario.Delegate.DelegatedScopes, request.Subject.Actor.Scopes);

        Assert.NotNull(request.Context.Delegation);
        var del = request.Context.Delegation!;
        Assert.Equal(grant.Id.ToString(), del.GrantId);
        Assert.Equal(grant.ManagerId, del.ManagerId);
        Assert.Equal(grant.DelegateId, del.DelegateId);
        Assert.Equal(grant.ExpiresAt, del.ExpiresAt);
        Assert.Equal(grant.Scopes, del.Scopes);
        Assert.Equal(now, request.Context.Now);

        // The manager subject identity is preserved (the delegate acts FOR the manager, not AS itself).
        Assert.Equal(scenario.Manager.Id, request.Subject.Id);
        Assert.Equal(scenario.Manager.Roles, request.Subject.Roles);
        Assert.Equal(scenario.Manager.Tenant, request.Subject.Tenant);
    }

    [Fact]
    public void Manager_direct_and_delegate_requests_differ_only_by_actor_delegation_and_clock()
    {
        var scenario = DelegationModel.Scenarios[0];
        var now = new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        var grant = Grant(
            scenario.Manager.Id, scenario.Delegate.Id,
            scenario.Delegate.DelegatedScopes.ToArray(), now.AddHours(1));

        var direct = DelegationModel.BuildManagerDirectRequest(scenario);
        var delegated = DelegationModel.BuildDelegateRequest(scenario, grant, now);

        Assert.Equal(direct.Action, delegated.Action);
        Assert.Equal(direct.Resource, delegated.Resource);
        Assert.Equal(direct.Context.Scopes, delegated.Context.Scopes);
        // Same subject once the Actor is cleared; same context once Delegation + Now are cleared.
        Assert.Equal(direct.Subject, delegated.Subject with { Actor = null });
        Assert.Equal(direct.Context, delegated.Context with { Delegation = null, Now = null });
    }

    [Fact]
    public void ToContextGrant_maps_the_grant_1_to_1()
    {
        var grant = Grant("user-manager1", "user-delegate1",
            [DelegationModel.AgentScopeRead], new DateTimeOffset(2026, 7, 4, 13, 0, 0, TimeSpan.Zero));

        var mapped = DelegationModel.ToContextGrant(grant);

        Assert.Equal(grant.Id.ToString(), mapped.GrantId);
        Assert.Equal(grant.ManagerId, mapped.ManagerId);
        Assert.Equal(grant.DelegateId, mapped.DelegateId);
        Assert.Equal(grant.ExpiresAt, mapped.ExpiresAt);
        Assert.Equal(grant.Scopes, mapped.Scopes);
    }

    [Fact]
    public void JustAfterExpiry_is_one_second_past_the_grant_expiry()
    {
        var expiresAt = new DateTimeOffset(2026, 7, 4, 13, 0, 0, TimeSpan.Zero);
        var grant = Grant("user-manager1", "user-delegate1", [DelegationModel.AgentScopeRead], expiresAt);

        Assert.Equal(expiresAt.AddSeconds(1), DelegationModel.JustAfterExpiry(grant));
        // The advanced clock is strictly past expiry, so the PDP's `Now < ExpiresAt` fails closed.
        Assert.True(DelegationModel.JustAfterExpiry(grant) >= grant.ExpiresAt);
    }

    [Fact]
    public void BuildCreateRequest_couples_the_grant_to_the_scenario_manager_delegate_and_scopes()
    {
        foreach (var scenario in DelegationModel.Scenarios)
        {
            var create = DelegationModel.BuildCreateRequest(scenario);

            Assert.Equal(scenario.Manager.Id, create.ManagerId);
            Assert.Equal(scenario.Delegate.Id, create.DelegateId);
            Assert.Equal(scenario.Manager.Tenant, create.TenantCode);
            Assert.Equal(scenario.Delegate.DelegatedScopes, create.Scopes);
            Assert.True(create.DurationMinutes > 0);
            Assert.NotEqual(create.ManagerId, create.DelegateId);
        }
    }

    [Fact]
    public void ToView_fails_closed_to_deny_when_decision_is_null()
    {
        var view = DelegationModel.ToView(null);

        Assert.Equal("Deny", view.Verdict);
        Assert.Equal("Unavailable", view.ReasonCode);
        Assert.False(view.IsDelegationNotActive);
    }

    [Fact]
    public void ToView_maps_a_permit_with_its_primary_reason()
    {
        var decision = new PdpDecisionDto(
            "Permit", [new PdpReasonDto("Permit", "allowed")], Obligations: null);

        var view = DelegationModel.ToView(decision);

        Assert.Equal("Permit", view.Verdict);
        Assert.Equal("Permit", view.ReasonCode);
        Assert.False(view.IsDelegationNotActive);
    }

    [Fact]
    public void ToView_surfaces_a_delegation_not_active_deny()
    {
        var decision = new PdpDecisionDto(
            "Deny",
            [new PdpReasonDto(DelegationModel.ReasonDelegationNotActive, "grant is not active")],
            Obligations: null);

        var view = DelegationModel.ToView(decision);

        Assert.Equal("Deny", view.Verdict);
        Assert.Equal(DelegationModel.ReasonDelegationNotActive, view.ReasonCode);
        Assert.True(view.IsDelegationNotActive);
    }

    [Fact]
    public void ToView_tolerates_a_decision_with_no_reasons()
    {
        var view = DelegationModel.ToView(new PdpDecisionDto("Deny", [], Obligations: null));

        Assert.Equal("Deny", view.Verdict);
        Assert.Equal("(none)", view.ReasonCode);
    }
}
