namespace AuthzEntitlements.Audit.Service.Contracts;

// Wire DTO for POST /api/audit/decisions. Mirrors PdpDecisionAuditEvent's fields exactly so
// the PDP HTTP-forwarding sink can serialize its event straight onto this contract. There is
// deliberately NO Producer field: the server stamps Producer = "pdp" for this endpoint so a
// caller can never spoof the producer identity that the tamper-evident chain records.
public sealed record IngestDecisionRequest(
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
    // CS36 (LRN-057): the PDP's canonical JSON snapshot of the full AccessRequest, forwarded so the
    // Audit Explorer can replay the decision faithfully. Additive/defaulted (older producers and the
    // non-PDP audit sources simply omit it -> null). It is persisted NON-hashed and is NOT part of
    // AuditPayload/ComputeRowHash; the server size-guards it before persisting (see AuditEndpoints).
    string? RequestSnapshot = null);
