namespace AuthzEntitlements.Audit.Service.Data;

// A single append-only, hash-chained audit row. Sequence is the chain position (assigned by
// the single writer, never database-generated). PrevHash/RowHash form the tamper-evident
// linkage; ReceivedAtUtc is the server receive time and is deliberately NOT part of the row
// hash (it is observability metadata, not producer-supplied content).
public sealed class AuditEntry
{
    public long Sequence { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }

    public string TraceId { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string SubjectId { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string ResourceType { get; set; } = string.Empty;

    public string? ResourceId { get; set; }

    public string Decision { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string? Tenant { get; set; }

    public string Producer { get; set; } = string.Empty;

    public string PrevHash { get; set; } = string.Empty;

    public string RowHash { get; set; } = string.Empty;

    public DateTimeOffset ReceivedAtUtc { get; set; }
}
