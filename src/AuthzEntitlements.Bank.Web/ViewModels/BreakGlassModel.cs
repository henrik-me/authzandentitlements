using AuthzEntitlements.Bank.Web.Clients;

namespace AuthzEntitlements.Bank.Web.ViewModels;

// Pure, dependency-free helpers for the CS21 "Break-glass" (emergency elevation) showcase. Kept
// out of the .razor so the demo scenarios, the base-vs-elevated request authoring, the issued-grant
// -> PDP-context mapping, and the fail-closed decision mapping are unit-testable offline (no server,
// Docker, Keycloak, or wall clock).
//
// The page makes the core fintech control visible: a base Deny for a MISSING CAPABILITY
// (MissingScope / RoleNotAuthorized) is raised to a Permit ONLY when an active, matching break-glass
// grant is present — carrying reason BreakGlassInvoked and the mandatory RequireBreakGlassReview
// obligation — while an integrity invariant (tenant isolation, maker-checker / SoD, subject-is-maker,
// pending-status) is NEVER overridden. The grant is issued by the Governance.Service; this model maps
// the issued grant onto the PDP EvaluationContext so the SAME action re-evaluates to a permit.
// Fail-closed: a null decision (PDP unreachable or non-2xx) maps to a Deny, never a silent allow.
public static class BreakGlassModel
{
    // Action + scope vocabulary, mirrored as literals from the PDP contract (Bank.Web mirrors the
    // PDP DTOs and does not reference Authz.Pdp, so these are literal copies of ActionNames /
    // ScopeNames).
    public const string ActionAccountRead = "bank.account.read";
    public const string ActionAccountCreate = "bank.account.create";

    public const string ScopeRead = "bank.read";

    // The reason code the PDP stamps on a break-glass permit and the obligation it attaches — literal
    // copies of ReasonCodes.BreakGlassInvoked / ObligationIds.RequireBreakGlassReview so the page can
    // recognise and highlight them without referencing Authz.Pdp.
    public const string ReasonBreakGlassInvoked = "BreakGlassInvoked";
    public const string ObligationRequireBreakGlassReview = "require_break_glass_review";

    // The subject an emergency grant elevates (id, roles, tenant, optional branch). Ids mirror the
    // governance/bank seed ("user-teller1") so an issued grant's PrincipalId matches the PDP Subject.Id.
    public sealed record DemoUser(string Id, IReadOnlyList<string> Roles, string Tenant, string? Branch = null);

    // One demo scenario: the human, the action + resource, the coarse human scopes the token carries,
    // the emergency justification, and the grant window. The base decision is a MISSING-CAPABILITY Deny
    // that an active break-glass grant elevates; the authoritative verdict comes from the live PDP.
    public sealed record Scenario(
        string Title,
        string Explanation,
        DemoUser User,
        string Action,
        PdpResourceDto Resource,
        IReadOnlyList<string> HumanScopes,
        string Justification,
        int DurationMinutes);

    private static readonly DemoUser Teller =
        new("user-teller1", ["Teller"], "CONTOSO", "NM01");

    // The demo scenarios, in stable display order. Each is a base Deny for a missing capability that an
    // active break-glass grant elevates — mirroring the reference PDP's BreakGlassDelegationScenarioCatalog
    // "break-glass-elevates-missing-scope" and "break-glass-elevates-role-not-authorized" cases.
    public static readonly IReadOnlyList<Scenario> Scenarios =
    [
        new Scenario(
            "Teller reads an account without the read scope",
            "The teller's token carries no bank.read scope, so the base read is denied MissingScope. " +
            "An active break-glass grant for exactly this subject + action elevates it to a permit, " +
            "carrying BreakGlassInvoked and the mandatory review obligation.",
            Teller,
            ActionAccountRead,
            new PdpResourceDto("account", Tenant: "CONTOSO"),
            [],
            "Incident INC-4021: read customer account to triage a failed settlement.",
            30),

        new Scenario(
            "Teller opens a new account without the manager role",
            "Creating an account requires the BranchManager role, so a teller is denied " +
            "RoleNotAuthorized. An active break-glass grant elevates this missing-capability deny to a " +
            "permit — but it would NOT override an integrity rule such as tenant isolation.",
            Teller,
            ActionAccountCreate,
            new PdpResourceDto("account", Tenant: "CONTOSO"),
            [ScopeRead],
            "Incident INC-4022: open a replacement account for a locked-out customer.",
            30),
    ];

    // Builds the BASE request for a scenario: EvaluationContext carries only the coarse scopes — no
    // break-glass grant and no injected clock — so the PDP returns the un-elevated deny.
    public static PdpAccessRequestDto BuildBaseRequest(Scenario scenario) =>
        new(
            new PdpSubjectDto(
                "user", scenario.User.Id, scenario.User.Roles,
                scenario.User.Tenant, scenario.User.Branch, Actor: null),
            new PdpActionDto(scenario.Action),
            scenario.Resource,
            new PdpContextDto(scenario.HumanScopes));

    // Builds the ELEVATED request: the SAME base request plus Context.BreakGlass mirroring the issued
    // grant and Context.Now = now (the injected decision clock the PDP checks expiry against). Differs
    // from BuildBaseRequest ONLY by the break-glass grant + clock, so the elevation is attributable to
    // exactly the emergency grant.
    public static PdpAccessRequestDto BuildBreakGlassRequest(
        Scenario scenario, BreakGlassGrantResponse grant, DateTimeOffset now) =>
        new(
            new PdpSubjectDto(
                "user", scenario.User.Id, scenario.User.Roles,
                scenario.User.Tenant, scenario.User.Branch, Actor: null),
            new PdpActionDto(scenario.Action),
            scenario.Resource,
            new PdpContextDto(scenario.HumanScopes, BreakGlass: ToContextGrant(grant), Now: now));

    // Authors the governance issue-request from the scenario so the grant is coupled to the exact
    // subject + action the PDP will re-evaluate: an active grant only elevates when its SubjectId ==
    // Subject.Id and its Action == the request action. The principal is the scenario's subject, never a
    // free-form field.
    public static IssueBreakGlassRequest BuildIssueRequest(Scenario scenario) =>
        new(
            scenario.User.Id,
            scenario.User.Tenant,
            scenario.Action,
            scenario.Justification,
            scenario.DurationMinutes);

    // Maps an issued governance break-glass grant to the PDP EvaluationContext grant shape, 1:1 by
    // field: the grant id (as string), the covered subject + action, the auto-expiry instant, and the
    // justification the permit reason surfaces for the audit trail.
    public static PdpBreakGlassGrantDto ToContextGrant(BreakGlassGrantResponse grant) =>
        new(
            grant.Id.ToString(),
            grant.PrincipalId,
            grant.Action,
            grant.ExpiresAt,
            grant.Justification);

    // A rendered decision: the verdict string, the primary reason code + message, and the obligation
    // ids (so the UI can highlight require_break_glass_review on a break-glass permit).
    public sealed record DecisionView(
        string Verdict,
        string ReasonCode,
        string Message,
        IReadOnlyList<string> Obligations)
    {
        public bool IsBreakGlass =>
            string.Equals(ReasonCode, ReasonBreakGlassInvoked, StringComparison.Ordinal);

        public bool RequiresBreakGlassReview =>
            Obligations.Contains(ObligationRequireBreakGlassReview, StringComparer.Ordinal);
    }

    // Maps a PDP decision to a view. Fail-closed: a null decision (PDP unreachable or non-2xx) becomes
    // a Deny with an explicit reason, never a silent allow. Otherwise the verdict, the primary reason
    // code + message, and every obligation id are surfaced.
    public static DecisionView ToView(PdpDecisionDto? decision) =>
        decision is null
            ? new DecisionView("Deny", "Unavailable", "PDP unavailable — fail-closed.", [])
            : new DecisionView(
                decision.Decision,
                decision.Reasons.Count > 0 ? decision.Reasons[0].Code : "(none)",
                decision.Reasons.Count > 0 ? decision.Reasons[0].Message : "(no reason)",
                decision.Obligations?.Select(o => o.Id).ToList() ?? []);
}
