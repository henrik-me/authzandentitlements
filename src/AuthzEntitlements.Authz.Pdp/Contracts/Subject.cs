namespace AuthzEntitlements.Authz.Pdp.Contracts;

// The AuthZEN "subject" — who is asking. The well-known fintech attributes the rules
// need (roles, tenant, branch) are modelled as first-class typed members rather than a
// loose property bag, so the reference provider and adapters read them without string
// spelunking. Type is the AuthZEN subject type, e.g. "user".
public sealed record Subject(
    string Type,
    string Id,
    IReadOnlyList<string> Roles,
    string? Tenant = null,
    string? Branch = null);
