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
    string? ActorType = null,
    // CS21 break-glass heightened audit: BreakGlass is true ONLY when this decision was an ACTUAL
    // break-glass elevation (a BreakGlassInvoked permit), not merely when a grant was present;
    // BreakGlassGrantId names the invoked grant on such a permit; DelegationId names any
    // manager->delegate grant carried in context. Additive with defaults so every existing positional
    // construction keeps compiling and the forwarded JSON simply gains optional fields — CS13's
    // Audit.Service hash-chain/schema is untouched (it tolerates extra fields).
    bool BreakGlass = false,
    string? BreakGlassGrantId = null,
    string? DelegationId = null,
    // CS36 faithful replay (LRN-057): a deterministic canonical JSON snapshot of the WHOLE
    // AccessRequest (subject/action/resource/context incl. every ABAC input) so the Audit Explorer
    // can replay the decision 1:1. Additive/defaulted so every existing positional construction keeps
    // compiling. Fail-open: null when serialization failed (the decision is still audited). It is
    // persisted NON-hashed (like the server's ReceivedAtUtc) and is never part of the tamper-evident
    // chain; the forwarded JSON simply gains an optional field the Audit.Service tolerates.
    string? RequestSnapshot = null);
