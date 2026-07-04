using AuthzEntitlements.Bank.Web.Clients;
using AuthzEntitlements.Bank.Web.ViewModels;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

// Offline unit tests for the CS21 "Break-glass" (emergency elevation) page model. No server, Docker,
// or Keycloak required — they exercise the demo scenarios, the base-vs-elevated request authoring
// (which must differ ONLY by the break-glass grant + injected clock), the issued-grant -> PDP-context
// mapping (which must be 1:1), and the fail-closed decision mapping (a null decision → Deny, a
// BreakGlassInvoked permit → Permit surfacing the require_break_glass_review obligation).
public class BreakGlassModelTests
{
    private static BreakGlassGrantResponse Grant(
        string principalId, string action, DateTimeOffset expiresAt) =>
        new(
            Guid.NewGuid(),
            principalId,
            "CONTOSO",
            action,
            "Emergency access for incident.",
            GrantedAt: expiresAt.AddMinutes(-30),
            ExpiresAt: expiresAt,
            ReviewedAt: null,
            ReviewedBy: null,
            ReviewOutcome: null,
            Active: true,
            RequiresReview: false,
            Status: "active");

    [Fact]
    public void Scenarios_are_non_empty_and_have_titles_and_justifications()
    {
        Assert.NotEmpty(BreakGlassModel.Scenarios);
        Assert.All(BreakGlassModel.Scenarios, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Title));
            Assert.False(string.IsNullOrWhiteSpace(s.Explanation));
            Assert.False(string.IsNullOrWhiteSpace(s.Justification));
            Assert.True(s.DurationMinutes > 0);
        });
    }

    [Fact]
    public void BuildBaseRequest_carries_no_break_glass_and_no_clock()
    {
        foreach (var scenario in BreakGlassModel.Scenarios)
        {
            var request = BreakGlassModel.BuildBaseRequest(scenario);

            Assert.Null(request.Subject.Actor);
            Assert.Null(request.Context.BreakGlass);
            Assert.Null(request.Context.Delegation);
            Assert.Null(request.Context.Now);
            Assert.Equal(scenario.Action, request.Action.Name);
            Assert.Equal(scenario.HumanScopes, request.Context.Scopes);
            Assert.Equal(scenario.User.Id, request.Subject.Id);
        }
    }

    [Fact]
    public void BuildBreakGlassRequest_mirrors_the_grant_fields_and_sets_now()
    {
        var scenario = BreakGlassModel.Scenarios[0];
        var now = new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        var grant = Grant(scenario.User.Id, scenario.Action, now.AddMinutes(30));

        var request = BreakGlassModel.BuildBreakGlassRequest(scenario, grant, now);

        Assert.NotNull(request.Context.BreakGlass);
        var bg = request.Context.BreakGlass!;
        Assert.Equal(grant.Id.ToString(), bg.GrantId);
        Assert.Equal(grant.PrincipalId, bg.SubjectId);
        Assert.Equal(grant.Action, bg.Action);
        Assert.Equal(grant.ExpiresAt, bg.ExpiresAt);
        Assert.Equal(grant.Justification, bg.Justification);
        Assert.Equal(now, request.Context.Now);
    }

    [Fact]
    public void Base_and_break_glass_requests_differ_only_by_the_grant_and_clock()
    {
        var scenario = BreakGlassModel.Scenarios[0];
        var now = new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);
        var grant = Grant(scenario.User.Id, scenario.Action, now.AddMinutes(30));

        var baseReq = BreakGlassModel.BuildBaseRequest(scenario);
        var bgReq = BreakGlassModel.BuildBreakGlassRequest(scenario, grant, now);

        // Same subject, action, resource, and scopes: the ONLY difference is Context.BreakGlass + Now.
        Assert.Equal(baseReq.Subject, bgReq.Subject);
        Assert.Equal(baseReq.Action, bgReq.Action);
        Assert.Equal(baseReq.Resource, bgReq.Resource);
        Assert.Equal(baseReq.Context.Scopes, bgReq.Context.Scopes);
        Assert.Equal(baseReq.Context with { }, bgReq.Context with { BreakGlass = null, Now = null });
    }

    [Fact]
    public void BuildIssueRequest_couples_the_grant_to_the_scenario_subject_and_action()
    {
        foreach (var scenario in BreakGlassModel.Scenarios)
        {
            var issue = BreakGlassModel.BuildIssueRequest(scenario);

            Assert.Equal(scenario.User.Id, issue.PrincipalId);
            Assert.Equal(scenario.User.Tenant, issue.TenantCode);
            Assert.Equal(scenario.Action, issue.Action);
            Assert.Equal(scenario.Justification, issue.Justification);
            Assert.Equal(scenario.DurationMinutes, issue.DurationMinutes);

            // The issued grant's principal + action are exactly what the elevated request's subject +
            // action will be, so an active grant matches (PDP requires SubjectId == Subject.Id and
            // Action == the request action).
            var baseReq = BreakGlassModel.BuildBaseRequest(scenario);
            Assert.Equal(baseReq.Subject.Id, issue.PrincipalId);
            Assert.Equal(baseReq.Action.Name, issue.Action);
        }
    }

    [Fact]
    public void ToContextGrant_maps_the_grant_1_to_1()
    {
        var grant = Grant("user-teller1", BreakGlassModel.ActionAccountRead,
            new DateTimeOffset(2026, 7, 4, 12, 30, 0, TimeSpan.Zero));

        var mapped = BreakGlassModel.ToContextGrant(grant);

        Assert.Equal(grant.Id.ToString(), mapped.GrantId);
        Assert.Equal(grant.PrincipalId, mapped.SubjectId);
        Assert.Equal(grant.Action, mapped.Action);
        Assert.Equal(grant.ExpiresAt, mapped.ExpiresAt);
        Assert.Equal(grant.Justification, mapped.Justification);
    }

    [Fact]
    public void ToView_fails_closed_to_deny_when_decision_is_null()
    {
        var view = BreakGlassModel.ToView(null);

        Assert.Equal("Deny", view.Verdict);
        Assert.Equal("Unavailable", view.ReasonCode);
        Assert.Empty(view.Obligations);
        Assert.False(view.IsBreakGlass);
        Assert.False(view.RequiresBreakGlassReview);
    }

    [Fact]
    public void ToView_maps_a_break_glass_permit_surfacing_the_review_obligation()
    {
        var decision = new PdpDecisionDto(
            "Permit",
            [new PdpReasonDto(BreakGlassModel.ReasonBreakGlassInvoked, "elevated")],
            [new PdpObligationDto(BreakGlassModel.ObligationRequireBreakGlassReview)]);

        var view = BreakGlassModel.ToView(decision);

        Assert.Equal("Permit", view.Verdict);
        Assert.Equal(BreakGlassModel.ReasonBreakGlassInvoked, view.ReasonCode);
        Assert.True(view.IsBreakGlass);
        Assert.True(view.RequiresBreakGlassReview);
        Assert.Contains(BreakGlassModel.ObligationRequireBreakGlassReview, view.Obligations);
    }

    [Fact]
    public void ToView_maps_a_missing_scope_deny_without_the_review_obligation()
    {
        var decision = new PdpDecisionDto(
            "Deny",
            [new PdpReasonDto("MissingScope", "missing bank.read")],
            Obligations: null);

        var view = BreakGlassModel.ToView(decision);

        Assert.Equal("Deny", view.Verdict);
        Assert.Equal("MissingScope", view.ReasonCode);
        Assert.False(view.IsBreakGlass);
        Assert.False(view.RequiresBreakGlassReview);
        Assert.Empty(view.Obligations);
    }

    [Fact]
    public void ToView_tolerates_a_decision_with_no_reasons()
    {
        var view = BreakGlassModel.ToView(new PdpDecisionDto("Deny", [], Obligations: null));

        Assert.Equal("Deny", view.Verdict);
        Assert.Equal("(none)", view.ReasonCode);
    }

    [Fact]
    public void First_scenario_is_a_missing_scope_deny_the_grant_can_elevate()
    {
        var scenario = BreakGlassModel.Scenarios[0];

        // No coarse read scope on the token → the base read denies MissingScope (an elevatable reason).
        Assert.DoesNotContain(BreakGlassModel.ScopeRead, scenario.HumanScopes);
        Assert.Equal(BreakGlassModel.ActionAccountRead, scenario.Action);
    }
}
