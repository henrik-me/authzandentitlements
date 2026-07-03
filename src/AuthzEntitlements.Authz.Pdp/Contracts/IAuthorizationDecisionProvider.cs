namespace AuthzEntitlements.Authz.Pdp.Contracts;

// The unified, AuthZEN-aligned PDP contract every engine answers. One question shape
// (subject/action/resource/context) in, one self-explaining decision (permit/deny +
// reasons + obligations) out. The reference provider is the in-process baseline; the
// adapter clickstops (CS06-CS09: Casbin, OpenFGA, OPA, Cedar) register alongside it and
// are selected by name via AuthorizationDecisionProviderFactory + PdpOptions.Provider —
// this is the seam that lets engines swap without changing calling code.
public interface IAuthorizationDecisionProvider
{
    // Stable, config-selectable engine name (e.g. "reference"). Matched
    // case-insensitively against PdpOptions.Provider by the factory.
    string Name { get; }

    // Evaluates one access request. Synchronous because the reference provider is a pure,
    // deterministic function of its input; out-of-process adapters may compute a result
    // asynchronously internally and return it here.
    AccessDecision Evaluate(AccessRequest request);
}
