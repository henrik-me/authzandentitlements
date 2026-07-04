namespace AuthzEntitlements.Authz.Pdp.Contracts;

// The AuthZEN "subject" — who is asking. The well-known fintech attributes the rules
// need (roles, tenant, branch) are modelled as first-class typed members rather than a
// loose property bag, so the reference provider and adapters read them without string
// spelunking. Type is the AuthZEN subject type, e.g. "user".
//
// Actor is the optional on-behalf-of (OBO) delegate: null => a direct call (a human, or a
// non-human acting AS ITSELF via Type = "service"|"agent"); non-null => this human Subject is
// being acted for BY the Actor, and the reference provider constrains the decision to the
// intersection of the human's rights and the agent's delegated scopes. Adding this defaulted
// trailing member keeps every existing positional construction compiling unchanged, so the
// direct/human path stays byte-identical.
public sealed record Subject(
    string Type,
    string Id,
    IReadOnlyList<string> Roles,
    string? Tenant = null,
    string? Branch = null,
    Actor? Actor = null);
