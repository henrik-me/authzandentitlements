namespace AuthzEntitlements.Bank.Api.Auth;

// The audit-ready record the fine-grained gate (Bank.Api) produces for each
// /api authorization decision (allow or deny). Bank.Api is the TERMINAL fine
// decider, so its own 401/403 ARE its authorization decisions. Emitted structured
// (one log event per /api request) so CS13's Audit.Service can ingest it verbatim —
// there is no live Audit.Service yet. Nullable Subject/Tenant fail open to null
// rather than fabricate a value the token did not carry.
public sealed record BankAuditEvent(
    DateTimeOffset TimestampUtc,
    string TraceId,
    string Method,
    string Path,
    string Decision,
    string Reason,
    string? Subject,
    string? Tenant,
    int StatusCode);
