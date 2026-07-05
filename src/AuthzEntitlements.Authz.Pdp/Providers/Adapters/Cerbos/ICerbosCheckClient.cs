using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters.Cerbos;

// The narrow forward-decision seam CerbosDecisionProvider depends on (LRN-038), mirroring the OPA
// adapter's out-of-process boundary and the SpiceDB adapter's ISpiceDbCheckClient. CerbosCheckService
// is the production implementation — a sealed adapter over the live Cerbos gRPC client whose
// blank/malformed-Endpoint path throws before any call — so the provider's permit/deny/fail-closed
// mapping (and its CS16 explanation) could not be asserted in the OFFLINE suite. Extracting this
// one-member interface lets a test double force any (allowed, outputToken) outcome with no server, so
// the full-decision reason-code + obligation mapping is unit-testable offline.
public interface ICerbosCheckClient
{
    // Evaluate the AccessRequest against the Cerbos "bank" resource policy and return the engine's
    // effect + the matching rule's output token. Builds the Cerbos client lazily on first use and
    // fails closed (throws) on a blank/malformed endpoint — the provider turns any throw into a
    // fail-closed deny, never a permit.
    CerbosCheckOutcome Check(AccessRequest request);
}
