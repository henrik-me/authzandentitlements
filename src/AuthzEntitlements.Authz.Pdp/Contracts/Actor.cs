namespace AuthzEntitlements.Authz.Pdp.Contracts;

// The non-human party acting FOR a human Subject in an on-behalf-of (OBO) delegation.
// Present on a Subject ONLY for OBO: an agent or workload identity performing an action on
// behalf of the human named by the Subject. Type is the delegate's kind ("agent" | "service"),
// Id is its stable identity, and Scopes are the DELEGATED capability scopes the delegate was
// granted (agent.bank.*) — the ceiling on what it may do for the user. A non-human acting AS
// ITSELF is modelled by Subject.Type ("service"|"agent") with Subject.Actor = null, not here.
public sealed record Actor(
    string Type,
    string Id,
    IReadOnlyList<string> Scopes);
