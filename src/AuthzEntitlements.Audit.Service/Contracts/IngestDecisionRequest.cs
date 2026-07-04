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
    string? Tenant);
