namespace AuthzEntitlements.Authz.Pdp.Contracts;

// A manager->delegate delegation grant, carried on EvaluationContext.Delegation (CS21). It authorizes
// a delegate to act on behalf of a manager for a bounded window, reusing the CS19 on-behalf-of (OBO)
// seam: ManagerId is the human Subject whose rights are borrowed, DelegateId is the Actor (the OBO
// principal) allowed to borrow them, and ExpiresAt is the auto-expiry instant checked against the
// INJECTED decision clock (EvaluationContext.Now) so expiry is a pure, deterministic function. Scopes
// are the capability scopes the manager delegated: the PDP requires the action's
// AgentScopeNames.RequiredFor(action) to be present in BOTH the Actor's scopes AND this grant's Scopes,
// so the delegate can exceed neither its own token nor the manager's grant. The effective decision is
// manager-rights (base) AND delegate-scopes (the CS19 OBO intersection) AND this grant's Scopes AND
// this active grant; an absent-but-required, expired, or mismatched grant denies DelegationNotActive,
// while a grant whose Scopes omit the required scope denies DelegationScopeMissing. GrantId identifies
// the grant for the audit trail and for revocation.
public sealed record DelegationGrant(
    string GrantId,
    string ManagerId,
    string DelegateId,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Scopes);
