namespace AuthzEntitlements.Authz.Pdp.Contracts;

// The AuthZEN "context" — request-time facts outside the subject/resource. For the
// fintech rules the coarse-grained granted scopes (e.g. "bank.read",
// "bank.transactions.write") are what the PDP still re-checks as defence in depth.
// Kept intentionally small; new contextual facts are added as members over time.
//
// CS21 adds three OPTIONAL, defaulted trailing members so the human path stays byte-identical
// (every existing positional construction still compiles and behaves unchanged): an optional
// break-glass emergency-elevation grant, an optional manager->delegate delegation grant, and the
// injected decision clock. Now is the SINGLE source of "the current time" for expiry — the provider
// never reads the wall clock, so a grant's expiry check (Now < ExpiresAt) is a pure, deterministic
// function of the request and is therefore fully testable.
public sealed record EvaluationContext(
    IReadOnlyList<string> Scopes,
    BreakGlassGrant? BreakGlass = null,
    DelegationGrant? Delegation = null,
    DateTimeOffset? Now = null);
