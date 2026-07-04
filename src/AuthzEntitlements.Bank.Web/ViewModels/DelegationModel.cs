using AuthzEntitlements.Bank.Web.Clients;

namespace AuthzEntitlements.Bank.Web.ViewModels;

// Pure, dependency-free helpers for the CS21 "Delegation" (manager -> delegate) showcase. Kept out
// of the .razor so the demo scenario, the manager-direct-vs-delegate request authoring, the issued-
// grant -> PDP-context mapping, and the fail-closed decision mapping are unit-testable offline (no
// server, Docker, Keycloak, or wall clock).
//
// Manager -> delegate delegation reuses the CS19 on-behalf-of (OBO) seam: the human Subject is the
// manager whose rights are borrowed, and Subject.Actor is the delegate borrowing them. The effective
// decision is manager-rights (base) AND delegate-scopes (the CS19 OBO intersection) AND an active,
// matching delegation grant (manager == Subject, delegate == Actor, Now < ExpiresAt). A supplied grant
// that is expired or names a different delegate denies DelegationNotActive — fail-closed.
//
// COMPOSITION NOTE (Wave A): an emergency break-glass elevation on the BASE decision does NOT remove
// the OBO constraints. A delegate acting under an elevated base still needs the delegated agent.bank.*
// scope AND (when one is supplied) an active delegation grant — break-glass grants missing capability,
// it does not widen delegation. The two controls compose; neither bypasses the other.
public static class DelegationModel
{
    // Action + scope vocabulary, mirrored as literals from the PDP contract (Bank.Web mirrors the PDP
    // DTOs and does not reference Authz.Pdp, so these are literal copies of ActionNames / ScopeNames /
    // AgentScopeNames).
    public const string ActionAccountRead = "bank.account.read";

    public const string ScopeRead = "bank.read";

    // The delegated capability scope the delegate must hold for a read (distinct from the manager's
    // coarse bank.read) — a literal copy of AgentScopeNames.Read.
    public const string AgentScopeRead = "agent.bank.read";

    // The reason code the PDP stamps when a supplied delegation grant is inactive (absent-but-required,
    // expired, or non-matching) — a literal copy of ReasonCodes.DelegationNotActive.
    public const string ReasonDelegationNotActive = "DelegationNotActive";

    // The manager whose rights the delegate borrows (id, roles, tenant, optional branch). Ids mirror
    // the governance/bank seed ("user-manager1") so an issued grant's ManagerId matches the PDP Subject.Id.
    public sealed record DemoManager(string Id, IReadOnlyList<string> Roles, string Tenant, string? Branch = null);

    // The delegate (the OBO Actor): its stable id and the delegated agent.bank.* scopes it was granted —
    // the ceiling on what it may do for the manager.
    public sealed record DemoDelegate(string Id, IReadOnlyList<string> DelegatedScopes);

    // One demo scenario: the manager, the delegate, the action + resource, the manager's coarse scopes,
    // and the grant window. The authoritative verdicts come from the live PDP.
    public sealed record Scenario(
        string Title,
        string Explanation,
        DemoManager Manager,
        DemoDelegate Delegate,
        string Action,
        PdpResourceDto Resource,
        IReadOnlyList<string> ManagerScopes,
        int DurationMinutes);

    private static readonly DemoManager Manager =
        new("user-manager1", ["BranchManager"], "CONTOSO", "NM01");

    private static readonly DemoDelegate Delegate =
        new("user-delegate1", [AgentScopeRead]);

    // The demo scenarios, in stable display order. Mirrors the reference PDP's
    // BreakGlassDelegationScenarioCatalog "delegation-active-grant-permits" case: a delegate holding the
    // delegated read scope acts for a manager under an active grant.
    public static readonly IReadOnlyList<Scenario> Scenarios =
    [
        new Scenario(
            "Delegate reads an account for the manager",
            "The manager may read the account, and the delegate holds the delegated agent.bank.read " +
            "scope — so under an active manager->delegate grant the delegate is permitted. Remove or " +
            "expire the grant and the same call is denied DelegationNotActive (fail-closed).",
            Manager,
            Delegate,
            ActionAccountRead,
            new PdpResourceDto("account", Tenant: "CONTOSO"),
            [ScopeRead],
            60),
    ];

    // Builds the MANAGER-DIRECT request: the manager acts for themselves — Subject.Actor is null and no
    // delegation grant is in context — so the PDP returns the manager's own base decision.
    public static PdpAccessRequestDto BuildManagerDirectRequest(Scenario scenario) =>
        new(
            new PdpSubjectDto(
                "user", scenario.Manager.Id, scenario.Manager.Roles,
                scenario.Manager.Tenant, scenario.Manager.Branch, Actor: null),
            new PdpActionDto(scenario.Action),
            scenario.Resource,
            new PdpContextDto(scenario.ManagerScopes));

    // Builds the DELEGATE (OBO) request: the SAME manager Subject, but with Subject.Actor set to the
    // delegate (its id + delegated scopes) and Context.Delegation mirroring the issued grant, with
    // Context.Now = now (the injected decision clock expiry is checked against). Differs from
    // BuildManagerDirectRequest by the Actor + the delegation grant + the clock. Supply an expired grant
    // (now >= ExpiresAt) or one naming a different delegate and the PDP denies DelegationNotActive.
    public static PdpAccessRequestDto BuildDelegateRequest(
        Scenario scenario, DelegationGrantResponse grant, DateTimeOffset now) =>
        new(
            new PdpSubjectDto(
                "user", scenario.Manager.Id, scenario.Manager.Roles,
                scenario.Manager.Tenant, scenario.Manager.Branch,
                Actor: new PdpActorDto("user", scenario.Delegate.Id, scenario.Delegate.DelegatedScopes)),
            new PdpActionDto(scenario.Action),
            scenario.Resource,
            new PdpContextDto(scenario.ManagerScopes, Delegation: ToContextGrant(grant), Now: now));

    // Authors the governance create-request from the scenario so the grant is coupled to the exact
    // manager -> delegate pair and delegated scopes the PDP will re-evaluate. The manager and delegate
    // are the scenario's identities, never free-form fields.
    public static CreateDelegationRequest BuildCreateRequest(Scenario scenario) =>
        new(
            scenario.Manager.Id,
            scenario.Delegate.Id,
            scenario.Manager.Tenant,
            scenario.Delegate.DelegatedScopes,
            scenario.DurationMinutes);

    // Maps an issued governance delegation grant to the PDP EvaluationContext grant shape, 1:1 by
    // field: the grant id (as string), the manager and delegate ids, the auto-expiry instant, and the
    // delegated Scopes the manager granted — which the PDP requires to contain the action's required
    // scope (the manager's grant bounds the delegate, distinct from the Actor's own token).
    public static PdpDelegationGrantDto ToContextGrant(DelegationGrantResponse grant) =>
        new(
            grant.Id.ToString(),
            grant.ManagerId,
            grant.DelegateId,
            grant.ExpiresAt,
            grant.Scopes);

    // The instant one second past a grant's expiry, used by the page to demonstrate the injected
    // decision clock + auto-expiry: re-evaluating the delegate request with this clock denies
    // DelegationNotActive without any wall-clock wait.
    public static DateTimeOffset JustAfterExpiry(DelegationGrantResponse grant) =>
        grant.ExpiresAt.AddSeconds(1);

    // A rendered decision: the verdict string plus the primary reason code + message.
    public sealed record DecisionView(string Verdict, string ReasonCode, string Message)
    {
        public bool IsDelegationNotActive =>
            string.Equals(ReasonCode, ReasonDelegationNotActive, StringComparison.Ordinal);
    }

    // Maps a PDP decision to a view. Fail-closed: a null decision (PDP unreachable or non-2xx) becomes a
    // Deny with an explicit reason, never a silent allow. Otherwise the verdict plus the primary reason
    // code + message are surfaced.
    public static DecisionView ToView(PdpDecisionDto? decision) =>
        decision is null
            ? new DecisionView("Deny", "Unavailable", "PDP unavailable — fail-closed.")
            : new DecisionView(
                decision.Decision,
                decision.Reasons.Count > 0 ? decision.Reasons[0].Code : "(none)",
                decision.Reasons.Count > 0 ? decision.Reasons[0].Message : "(no reason)");
}
