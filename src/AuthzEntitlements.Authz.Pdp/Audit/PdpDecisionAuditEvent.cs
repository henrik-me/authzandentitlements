namespace AuthzEntitlements.Authz.Pdp.Audit;

// The audit-ready record every PDP decision produces (permit or deny). Emitted structured
// (one event per evaluation) so CS13's Audit.Service can ingest it verbatim — there is no
// live Audit.Service yet. Nullable ResourceId/Tenant fail open to null rather than
// fabricate a value the request did not carry.
public sealed record PdpDecisionAuditEvent(
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
    // CS16 explainability: the normalized determining rule, the engine-native policy reference(s)
    // flattened as "kind:reference" strings (audit-ingestion-friendly), and the human narrative.
    string DeterminingRule,
    IReadOnlyList<string> PolicyReferences,
    string Narrative);
