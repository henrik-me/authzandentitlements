namespace AuthzEntitlements.Edge.Gateway.Audit;

// The audit-ready record a gateway coarse decision produces. Emitted structured
// (one log event per proxied /api request) so CS13's Audit.Service can ingest it
// verbatim — there is no live Audit.Service yet. Nullable fields fail open to
// null rather than fabricate a value the token did not carry.
public sealed record GatewayAuditEvent(
    DateTimeOffset TimestampUtc,
    string TraceId,
    string Method,
    string Path,
    string? RouteId,
    string Decision,
    string Reason,
    string? Subject,
    string? Tenant,
    string? RequiredScope,
    string Audience);
