using AuthzEntitlements.Bank.Web.Clients;
using AuthzEntitlements.Bank.Web.ViewModels;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

// Offline unit tests for the CS19 "Agent access" (on-behalf-of / non-human access) page model. No
// server, Docker, or Keycloak required — they exercise the demo scenarios, the human-vs-agent
// request authoring (which must differ ONLY by Subject.Actor), and the fail-closed decision
// mapping (a null decision → Deny with a clear reason, never a silent allow).
public class AgentAccessModelTests
{
    [Fact]
    public void Scenarios_are_non_empty_and_cover_the_four_demo_cases()
    {
        Assert.Equal(4, AgentAccessModel.Scenarios.Count);
        Assert.All(AgentAccessModel.Scenarios, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Title));
            Assert.False(string.IsNullOrWhiteSpace(s.Explanation));
        });
    }

    [Fact]
    public void BuildHumanRequest_has_null_actor()
    {
        foreach (var scenario in AgentAccessModel.Scenarios)
        {
            var request = AgentAccessModel.BuildHumanRequest(scenario);
            Assert.Null(request.Subject.Actor);
        }
    }

    [Fact]
    public void BuildAgentRequest_carries_the_scenario_agent_id_and_delegated_scopes()
    {
        foreach (var scenario in AgentAccessModel.Scenarios)
        {
            var request = AgentAccessModel.BuildAgentRequest(scenario);

            Assert.NotNull(request.Subject.Actor);
            Assert.Equal("agent", request.Subject.Actor!.Type);
            Assert.Equal(scenario.Agent.Id, request.Subject.Actor.Id);
            Assert.Equal(scenario.Agent.DelegatedScopes, request.Subject.Actor.Scopes);
        }
    }

    [Fact]
    public void Human_and_agent_requests_differ_only_by_subject_actor()
    {
        foreach (var scenario in AgentAccessModel.Scenarios)
        {
            var human = AgentAccessModel.BuildHumanRequest(scenario);
            var agent = AgentAccessModel.BuildAgentRequest(scenario);

            // Same everything except Subject.Actor: comparing the two subjects with Actor cleared
            // proves the ONLY difference is the delegate.
            Assert.Equal(human.Action, agent.Action);
            Assert.Equal(human.Resource, agent.Resource);
            Assert.Equal(human.Context, agent.Context);
            Assert.Equal(human.Subject, agent.Subject with { Actor = null });

            Assert.Null(human.Subject.Actor);
            Assert.NotNull(agent.Subject.Actor);
        }
    }

    [Fact]
    public void BuildAgentRequest_preserves_the_human_subject_identity()
    {
        var scenario = AgentAccessModel.Scenarios[0];
        var agent = AgentAccessModel.BuildAgentRequest(scenario);

        Assert.Equal("user", agent.Subject.Type);
        Assert.Equal(scenario.User.Id, agent.Subject.Id);
        Assert.Equal(scenario.User.Roles, agent.Subject.Roles);
        Assert.Equal(scenario.User.Tenant, agent.Subject.Tenant);
        Assert.Equal(scenario.User.Branch, agent.Subject.Branch);
    }

    [Fact]
    public void BuildRequests_use_the_scenario_action_and_human_scopes()
    {
        var scenario = AgentAccessModel.Scenarios[1];

        var human = AgentAccessModel.BuildHumanRequest(scenario);
        var agent = AgentAccessModel.BuildAgentRequest(scenario);

        Assert.Equal(scenario.Action, human.Action.Name);
        Assert.Equal(scenario.Action, agent.Action.Name);
        Assert.Equal(scenario.HumanScopes, human.Context.Scopes);
        Assert.Equal(scenario.HumanScopes, agent.Context.Scopes);
    }

    [Fact]
    public void ToView_fails_closed_to_deny_when_decision_is_null()
    {
        var view = AgentAccessModel.ToView(null);

        Assert.Equal("Deny", view.Verdict);
        Assert.Equal("PDP unavailable — fail-closed.", view.Message);
    }

    [Fact]
    public void ToView_maps_a_permit_with_its_primary_reason()
    {
        var decision = new PdpDecisionDto(
            "Permit",
            [new PdpReasonDto("Permit", "allowed")],
            Obligations: null);

        var view = AgentAccessModel.ToView(decision);

        Assert.Equal("Permit", view.Verdict);
        Assert.Equal("Permit", view.ReasonCode);
        Assert.Equal("allowed", view.Message);
    }

    [Fact]
    public void ToView_maps_a_deny_surfacing_the_reason_code()
    {
        var decision = new PdpDecisionDto(
            "Deny",
            [new PdpReasonDto("DelegationScopeMissing", "agent lacks the delegated scope")],
            Obligations: null);

        var view = AgentAccessModel.ToView(decision);

        Assert.Equal("Deny", view.Verdict);
        Assert.Equal("DelegationScopeMissing", view.ReasonCode);
        Assert.Equal("agent lacks the delegated scope", view.Message);
    }

    [Fact]
    public void ToView_tolerates_a_decision_with_no_reasons()
    {
        var decision = new PdpDecisionDto("Deny", [], Obligations: null);

        var view = AgentAccessModel.ToView(decision);

        Assert.Equal("Deny", view.Verdict);
        Assert.Equal("(none)", view.ReasonCode);
    }

    [Fact]
    public void The_missing_delegated_scope_scenario_grants_the_human_a_write_scope_but_the_agent_only_read()
    {
        var scenario = AgentAccessModel.Scenarios[1];

        Assert.Contains(AgentAccessModel.HumanScopeTransactionsWrite, scenario.HumanScopes);
        Assert.Contains(AgentAccessModel.AgentScopeRead, scenario.Agent.DelegatedScopes);
        Assert.DoesNotContain(
            AgentAccessModel.AgentScopeTransactionsWrite, scenario.Agent.DelegatedScopes);
    }

    [Fact]
    public void The_cross_tenant_scenario_targets_a_different_tenant_than_the_user()
    {
        var scenario = AgentAccessModel.Scenarios[2];

        Assert.NotEqual(scenario.User.Tenant, scenario.Resource.Tenant);
    }
}
