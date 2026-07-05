using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters.Topaz;

// The narrow forward-decision seam TopazDecisionProvider depends on (LRN-038), mirroring the OPA
// adapter's out-of-process boundary and the Cerbos adapter's ICerbosCheckClient. TopazCheckService is
// the production implementation — a sealed adapter over the live Aserto authorizer gRPC client whose
// blank/malformed-Endpoint path throws before any call — so the provider's permit/deny/fail-closed
// mapping (and its CS16 explanation) could not otherwise be asserted in the OFFLINE suite. Extracting
// this one-member interface lets a test double force any decision outcome (or a thrown engine error)
// with no server, so the full-decision reason-code + obligation mapping is unit-testable offline.
public interface ITopazCheckClient
{
    // Evaluate the AccessRequest against the Topaz OPA bundle's data.authz.bank.decision rule and return
    // the raw Rego decision object it produced. Builds the authorizer client lazily on first use and
    // fails closed (throws) on a blank/malformed endpoint — the provider turns any throw into a
    // fail-closed deny, never a permit.
    TopazCheckOutcome Check(AccessRequest request);
}

// The minimal result the Topaz forward-check seam returns to the provider: the raw fields of the Rego
// decision object Topaz's OPA bundle emits under data.authz.bank.decision — the SAME shape the OPA
// adapter maps, because Topaz reuses the SAME Rego policy. All members are nullable so a malformed,
// partial, or absent decision is DETECTED (and fails closed) rather than throwing or silently
// permitting:
//   * Decision    — "Permit" or "Deny"; any other value (or null) is unrecognized and fails closed.
//   * Reason      — the stable reason code (e.g. "Permit", "MissingScope", "MakerEqualsChecker").
//   * Rule        — the determining check id (CS16), "<action-short>.<Reason>" (additive; a policy that
//                   predates the field yields null and the explanation degrades to the package path).
//   * Obligations — obligation ids for a permitted transaction.create ("require_approval" /
//                   "post_immediately"); absent or empty otherwise.
//   * ObligationsMalformed — true when the authorizer returned an `obligations` field that was PRESENT
//                   but NOT a JSON array (e.g. a bare string). The provider FAILS CLOSED on such a permit
//                   rather than treating it as a no-obligation permit — silently dropping an obligation
//                   like require_approval would be a fail-OPEN on the maker-checker threshold. An absent
//                   or list-valued obligations field leaves this false (its own None/list handling stands).
//
// TopazCheckOutcome.None is the sentinel the service returns when the authorizer produced NO usable
// decision binding (an empty query result — the policy was undefined for the input, or the response was
// structurally malformed). The provider maps None to a fail-closed deny, mirroring the OPA adapter's
// treatment of an absent "result".
public sealed record TopazCheckOutcome(
    string? Decision,
    string? Reason,
    string? Rule,
    IReadOnlyList<string>? Obligations,
    bool ObligationsMalformed = false)
{
    public static readonly TopazCheckOutcome None = new(null, null, null, null);
}
