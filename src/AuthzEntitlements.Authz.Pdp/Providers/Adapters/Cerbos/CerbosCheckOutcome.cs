namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters.Cerbos;

// The minimal, engine-agnostic result the Cerbos forward-check seam returns to the provider: the
// EFFECT_ALLOW/EFFECT_DENY the engine reached for the requested action, plus the OPTIONAL output
// token the matching policy rule emitted (Cerbos `output.when.ruleActivated`).
//
// OutputToken carries the ordered-check verdict the full-decision Cerbos policy computes:
//   * a bare reason code — e.g. "MissingScope", "TenantMismatch", "MakerEqualsChecker" (a Deny), or
//     "Permit" (a permit with no obligation), OR
//   * "Permit:<obligation>" — e.g. "Permit:require_approval" / "Permit:post_immediately", the
//     threshold obligation on a permitted bank.transaction.create.
//
// OutputToken is NULL only when the policy produced no output for the action — i.e. Cerbos'
// default deny for an action with no matching rule (an unknown/unhandled action). The provider maps
// that to a fail-closed UnknownAction deny.
public sealed record CerbosCheckOutcome(bool Allowed, string? OutputToken);
