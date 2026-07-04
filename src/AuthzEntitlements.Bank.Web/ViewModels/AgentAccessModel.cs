using AuthzEntitlements.Bank.Web.Clients;

namespace AuthzEntitlements.Bank.Web.ViewModels;

// Pure, dependency-free helpers for the CS19 "Agent access" (on-behalf-of / non-human access)
// showcase. Kept out of the .razor so the demo scenarios, the human-vs-agent request authoring,
// and the fail-closed decision mapping are unit-testable offline (no server, Docker, or Keycloak).
//
// The point of the page is to make constrained delegation visible: the SAME action is evaluated
// two ways — as the human directly (Subject.Actor = null) and via the agent (Subject.Actor set) —
// and the two requests differ ONLY by Subject.Actor. The agent is constrained to the intersection
// of the user's rights and its delegated agent.bank.* scopes; the human path is unaffected.
// Fail-closed: a null decision (PDP unreachable or non-2xx) maps to a Deny with a clear reason,
// never a silent allow.
public static class AgentAccessModel
{
    // Action + coarse-scope + delegated-scope vocabulary, mirrored as literals from the PDP
    // contract (Bank.Web mirrors the PDP DTOs and does not reference Authz.Pdp, so these are
    // literal copies of ActionNames / ScopeNames / AgentScopeNames).
    public const string ActionAccountRead = "bank.account.read";
    public const string ActionTransactionCreate = "bank.transaction.create";

    public const string HumanScopeRead = "bank.read";
    public const string HumanScopeTransactionsWrite = "bank.transactions.write";

    public const string AgentScopeRead = "agent.bank.read";
    public const string AgentScopeTransactionsWrite = "agent.bank.transactions.write";

    // The effective human the agent acts FOR (id, roles, tenant, optional branch).
    public sealed record DemoUser(string Id, IReadOnlyList<string> Roles, string Tenant, string? Branch = null);

    // The non-human delegate (its stable id + the delegated agent.bank.* scopes it was granted —
    // the ceiling on what it may do for the user).
    public sealed record DemoAgent(string Id, IReadOnlyList<string> DelegatedScopes);

    // One demo scenario: a human, an action + resource, the coarse human scopes, and the agent
    // acting on the user's behalf. The expected human/agent verdicts are described in Explanation
    // for the reader; the authoritative verdicts come from the live PDP.
    public sealed record Scenario(
        string Title,
        string Explanation,
        DemoUser User,
        string Action,
        PdpResourceDto Resource,
        IReadOnlyList<string> HumanScopes,
        DemoAgent Agent);

    private static readonly DemoUser Teller =
        new("user-teller1", ["Teller"], "CONTOSO", "NM01");

    private const string AgentId = "agent-copilot-1";

    // The demo scenarios, in stable display order. Each pairs a human-direct evaluation with an
    // agent (OBO) evaluation of the SAME action so the intersection semantics are visible side by
    // side.
    public static readonly IReadOnlyList<Scenario> Scenarios =
    [
        new Scenario(
            "Agent reads the user's in-tenant account",
            "The teller may read the account, and the agent holds the delegated agent.bank.read " +
            "scope — so both the human and the agent are permitted. The agent is acting within the " +
            "intersection of the user's rights and its delegation.",
            Teller,
            ActionAccountRead,
            new PdpResourceDto("account", Tenant: "CONTOSO"),
            [HumanScopeRead],
            new DemoAgent(AgentId, [AgentScopeRead])),

        new Scenario(
            "Agent tries to create a transaction with only read delegated",
            "The teller could create this transaction, but the agent holds only agent.bank.read — " +
            "not the agent.bank.transactions.write scope the action class requires. The human is " +
            "permitted; the agent is denied DelegationScopeMissing. Delegation is narrower than the user.",
            Teller,
            ActionTransactionCreate,
            new PdpResourceDto("transaction", Tenant: "CONTOSO", Amount: 250m, MakerId: Teller.Id),
            [HumanScopeTransactionsWrite],
            new DemoAgent(AgentId, [AgentScopeRead])),

        new Scenario(
            "Agent reads a DIFFERENT tenant's account for the user",
            "The account belongs to another tenant, so the human is denied TenantMismatch — and " +
            "because the agent can never exceed the user, the agent is denied for the same reason. " +
            "Delegation cannot widen the user's tenant scoping.",
            Teller,
            ActionAccountRead,
            new PdpResourceDto("account", Tenant: "FABRIKAM"),
            [HumanScopeRead],
            new DemoAgent(AgentId, [AgentScopeRead])),

        new Scenario(
            "Agent creates a small in-tenant transaction as the user",
            "The teller may create this transaction, and the agent holds the delegated " +
            "agent.bank.transactions.write scope — so both are permitted. A scoped, time-boxed " +
            "write delegation lets the agent act for exactly this action class.",
            Teller,
            ActionTransactionCreate,
            new PdpResourceDto("transaction", Tenant: "CONTOSO", Amount: 250m, MakerId: Teller.Id),
            [HumanScopeTransactionsWrite],
            new DemoAgent(AgentId, [AgentScopeTransactionsWrite])),
    ];

    // Builds the human-direct request for a scenario: Subject.Actor is null (the user acting
    // directly). This is byte-identical to the agent request except for the missing Actor.
    public static PdpAccessRequestDto BuildHumanRequest(Scenario scenario) =>
        new(
            new PdpSubjectDto(
                "user", scenario.User.Id, scenario.User.Roles,
                scenario.User.Tenant, scenario.User.Branch, Actor: null),
            new PdpActionDto(scenario.Action),
            scenario.Resource,
            new PdpContextDto(scenario.HumanScopes));

    // Builds the agent (OBO) request for a scenario: the SAME request but with Subject.Actor set to
    // the delegate (its id + delegated scopes). Differs from BuildHumanRequest ONLY by Subject.Actor.
    public static PdpAccessRequestDto BuildAgentRequest(Scenario scenario) =>
        new(
            new PdpSubjectDto(
                "user", scenario.User.Id, scenario.User.Roles,
                scenario.User.Tenant, scenario.User.Branch,
                Actor: new PdpActorDto("agent", scenario.Agent.Id, scenario.Agent.DelegatedScopes)),
            new PdpActionDto(scenario.Action),
            scenario.Resource,
            new PdpContextDto(scenario.HumanScopes));

    // A rendered decision: the verdict string, the primary reason code, and its message.
    public sealed record DecisionView(string Verdict, string ReasonCode, string Message);

    // Maps a PDP decision to a view. Fail-closed: a null decision (PDP unreachable or non-2xx)
    // becomes a Deny with an explicit reason, never a silent allow. Otherwise the verdict plus the
    // primary reason code + message are surfaced.
    public static DecisionView ToView(PdpDecisionDto? decision) =>
        decision is null
            ? new DecisionView("Deny", "Unavailable", "PDP unavailable — fail-closed.")
            : new DecisionView(
                decision.Decision,
                decision.Reasons.Count > 0 ? decision.Reasons[0].Code : "(none)",
                decision.Reasons.Count > 0 ? decision.Reasons[0].Message : "(no reason)");
}
