namespace AuthzEntitlements.Authz.Pdp.Contracts;

// A machine-stable Code plus a human Message explaining a decision. The Code is the
// contract other layers (audit, playground, tests, adapter parity checks) match on, so
// it must stay stable even if the Message wording changes.
public sealed record Reason(string Code, string Message);

// Stable reason codes shared by the reference provider and every adapter so a decision explains
// itself the same way across engines. Most map 1:1 to the Bank.Api enforcement rules; SodConflict
// is the CS11 governance segregation-of-duties verdict (it maps to no Bank.Api rule). UnknownAction
// is the fail-closed code for an action outside ActionNames.
public static class ReasonCodes
{
    public const string Permit = "Permit";
    public const string MissingScope = "MissingScope";
    public const string TenantMismatch = "TenantMismatch";
    public const string RoleNotAuthorized = "RoleNotAuthorized";
    public const string SubjectNotMaker = "SubjectNotMaker";
    public const string MakerEqualsChecker = "MakerEqualsChecker";
    public const string NotPending = "NotPending";
    public const string BranchNotInTenant = "BranchNotInTenant";

    // CS11 governance segregation-of-duties verdict: the reason a governance.access.request denies a
    // toxic role combination. Shared so the reference and OPA engines explain a SoD denial
    // identically — and so it survives the OPA adapter's bounded-reason allow-list.
    public const string SodConflict = "SodConflict";

    public const string UnknownAction = "UnknownAction";

    // CS19 constrained-delegation verdict: an on-behalf-of (OBO) request where the human Subject
    // is permitted but the acting Agent lacks the delegated scope required for the action class.
    // The agent can never exceed the user, and it must additionally hold the delegated capability —
    // this code is the reason it is denied when it does not.
    public const string DelegationScopeMissing = "DelegationScopeMissing";

    // CS21 break-glass emergency elevation: a base Deny for a MISSING CAPABILITY (MissingScope or
    // RoleNotAuthorized) was raised to a Permit by an active, matching break-glass grant. The permit
    // carries the RequireBreakGlassReview obligation (mandatory post-review). It is NEVER produced for
    // an integrity denial — break-glass grants a missing capability, it does not bypass tenant
    // isolation or segregation of duties.
    public const string BreakGlassInvoked = "BreakGlassInvoked";

    // CS21 manager->delegate delegation: an on-behalf-of request whose Actor is otherwise permitted
    // (it holds the delegated scope) is denied because the delegation grant in context is
    // absent-but-required, expired, or does not match this manager (Subject) -> delegate (Actor).
    // Fail-closed: any of those conditions denies rather than silently permitting.
    public const string DelegationNotActive = "DelegationNotActive";

    // CS45 extended-authorization fail-closed guard: a request carries CS19/CS21 extended-context
    // (Subject.Actor on-behalf-of, Context.Delegation, or Context.BreakGlass) but the selected engine
    // does NOT declare ISupportsExtendedAuthorizationContext — so it cannot honour those constraints.
    // AuthorizationDecisionProviderFactory wraps every non-capable provider in the
    // ExtendedContextGuardProvider, which denies with this code rather than forwarding a request the
    // engine would evaluate by the human subject alone (a fail-OPEN on an engine swap). It is
    // DELIBERATELY distinct from any provider-local "ProviderUnavailable"/"EngineUnavailable" code and
    // MUST NOT contain the substring "unavailable": PlaygroundFanoutService classifies deny reasons
    // containing "unavailable" as an engine OUTAGE and excludes them from its all-agree verdict, but
    // this is a deliberate, correct semantic boundary — a genuine deny, not an outage.
    public const string ExtendedContextUnsupported = "ExtendedContextUnsupported";
}
