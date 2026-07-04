namespace AuthzEntitlements.Audit.Service.Contracts;

// Response bodies for the audit API. IngestDecisionResponse echoes the assigned sequence and
// the prev/row hashes so a caller can locally record its position in the chain. The others
// back the verification and query endpoints.
public sealed record IngestDecisionResponse(long Sequence, string PrevHash, string RowHash);

public sealed record ChainVerificationResponse(
    bool Valid,
    long EntryCount,
    long? BrokenAtSequence,
    string? Reason,
    long? TailSequence = null,
    string? TailRowHash = null);

public sealed record AuditEntryView(
    long Sequence,
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
    string Producer,
    string PrevHash,
    string RowHash);
