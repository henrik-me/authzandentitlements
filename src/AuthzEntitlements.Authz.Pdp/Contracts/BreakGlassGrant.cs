namespace AuthzEntitlements.Authz.Pdp.Contracts;

// A break-glass emergency-elevation grant, carried on EvaluationContext.BreakGlass (CS21). It is an
// on-behalf-of-style delegation with an elevated, EXPIRING scope: for a bounded window the named
// SubjectId may perform the named Action even when the base decision denied it for a MISSING
// CAPABILITY (a missing scope or an ineligible role). GrantId identifies the grant for the audit
// trail and the mandatory post-review; ExpiresAt is the auto-expiry instant, checked against the
// INJECTED decision clock (EvaluationContext.Now) so expiry stays a pure, deterministic function
// rather than a wall-clock read; Justification is why the grant was issued (surfaced on the permit
// reason so the audit record explains the emergency access). Break-glass NEVER overrides an
// integrity invariant (tenant isolation, maker-checker / segregation of duties, subject-is-maker,
// pending-status, unknown-action) — see ReferenceDecisionProvider for the elevatable-reason set.
public sealed record BreakGlassGrant(
    string GrantId,
    string SubjectId,
    string Action,
    DateTimeOffset ExpiresAt,
    string Justification);
