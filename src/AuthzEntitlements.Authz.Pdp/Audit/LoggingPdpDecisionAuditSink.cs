namespace AuthzEntitlements.Authz.Pdp.Audit;

// Default audit sink: emit one structured ILogger event per PDP decision with every field
// named, so a log/OTel pipeline captures a queryable trail without a live Audit.Service in
// CS05. Mirrors BankAuthorizationAuditMiddleware's structured-emission style.
public sealed class LoggingPdpDecisionAuditSink(ILogger<LoggingPdpDecisionAuditSink> logger)
    : IPdpDecisionAuditSink
{
    public void Record(PdpDecisionAuditEvent decisionEvent) =>
        logger.LogInformation(
            "PDP decision {Decision} ({Reason}) provider={Provider} action={Action} " +
            "resource={ResourceType}/{ResourceId} subject={SubjectId} subjectType={SubjectType} " +
            "actorType={ActorType} actorId={ActorId} tenant={Tenant} " +
            "rule={DeterminingRule} policyReferences={PolicyReferences} narrative={Narrative} " +
            "trace={TraceId} at {TimestampUtc}",
            Clean(decisionEvent.Decision),
            Clean(decisionEvent.Reason),
            Clean(decisionEvent.Provider),
            Clean(decisionEvent.Action),
            Clean(decisionEvent.ResourceType),
            Clean(decisionEvent.ResourceId),
            Clean(decisionEvent.SubjectId),
            Clean(decisionEvent.SubjectType),
            Clean(decisionEvent.ActorType),
            Clean(decisionEvent.ActorId),
            Clean(decisionEvent.Tenant),
            Clean(decisionEvent.DeterminingRule),
            Clean(string.Join(" | ", decisionEvent.PolicyReferences)),
            Clean(decisionEvent.Narrative),
            Clean(decisionEvent.TraceId),
            decisionEvent.TimestampUtc);

    // Strip CR/LF from EVERY rendered string value before it reaches the log line, so no
    // request- or engine-derived field (e.g. an untrusted subject/actor id or type from the
    // anonymous /evaluate body, or an OpenFGA relationship-tuple policy reference that embeds
    // those ids) can inject a newline to forge a fake log entry (CWE-117 log injection). The
    // bounded/typed fields are cleaned too, for uniform defense in depth at negligible cost.
    // Only the human-readable log string is sanitized; the audit-of-record (PdpDecisionAuditEvent
    // -> Audit.Service hash chain) keeps the raw values.
    private static string? Clean(string? value) =>
        value?.Replace('\r', ' ').Replace('\n', ' ');
}
