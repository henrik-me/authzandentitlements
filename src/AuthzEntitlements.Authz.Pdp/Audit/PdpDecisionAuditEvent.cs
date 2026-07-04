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
    string Narrative,
    // CS19 agent/non-human access: the acting principal's kind and, for on-behalf-of (OBO) calls,
    // the delegate's identity. SubjectType is the Subject's own type ("user" for a human, or
    // "service"/"agent" for a non-human acting as itself). ActorId/ActorType are non-null ONLY on
    // an OBO call (a human Subject acted for by an Actor); null otherwise. Additive with defaults so
    // every existing positional construction keeps compiling and the forwarded JSON simply gains
    // optional fields the Audit.Service tolerates.
    string SubjectType = "user",
    string? ActorId = null,
    string? ActorType = null);
