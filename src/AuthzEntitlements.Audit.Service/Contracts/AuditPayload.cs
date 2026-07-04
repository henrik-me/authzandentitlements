namespace AuthzEntitlements.Audit.Service.Contracts;

// The full set of content fields that are persisted AND folded into the hash chain for a
// single audit entry. Producer distinguishes the ingestion source (e.g. "pdp"); it is set
// by the server per endpoint, never supplied by the wire caller. Nullable ResourceId/Tenant
// hash distinctly from empty strings so a null is never silently equated to "".
public sealed record AuditPayload(
    DateTimeOffset TimestampUtc,
    string TraceId,
    string Provider,
    string SubjectId,
    string Action,
    string ResourceType,
    string? ResourceId,
    string Decision,
    string Reason,
    string? Tenant,
    string Producer);
