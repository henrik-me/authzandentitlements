namespace AuthzEntitlements.Governance.Service.Sod;

// Local copies of the PDP's AuthZEN wire shape (docs/authz/pdp-contract.md). The
// governance service calls the PDP over HTTP only and does not reference its project, so
// it carries its own request/response records. Serialised with the ASP.NET web JSON
// defaults (camelCase), these produce exactly the fields the PDP deserializes:
// subject/action/resource/context in, decision/reasons/obligations out.

internal sealed record PdpAccessRequest(
    PdpSubject Subject,
    PdpAction Action,
    PdpResource Resource,
    PdpEvaluationContext Context);

internal sealed record PdpSubject(
    string Type,
    string Id,
    IReadOnlyList<string> Roles,
    string? Tenant);

internal sealed record PdpAction(string Name);

internal sealed record PdpResource(
    string Type,
    string Id,
    string? Tenant);

internal sealed record PdpEvaluationContext(IReadOnlyList<string> Scopes);

// Response: the decision is serialised by the PDP as an enum name ("Permit"/"Deny"), so
// it is read here as a string. Reasons/Obligations are nullable to fail closed on a
// malformed body (a null primary reason is treated as unavailable, not permit).
internal sealed record PdpAccessDecision(
    string? Decision,
    IReadOnlyList<PdpReason>? Reasons,
    IReadOnlyList<PdpObligation>? Obligations);

internal sealed record PdpReason(string Code, string Message);

internal sealed record PdpObligation(string Id);
