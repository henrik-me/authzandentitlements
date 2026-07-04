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
            decisionEvent.Decision,
            decisionEvent.Reason,
            decisionEvent.Provider,
            Clean(decisionEvent.Action),
            Clean(decisionEvent.ResourceType),
            Clean(decisionEvent.ResourceId),
            Clean(decisionEvent.SubjectId),
            Clean(decisionEvent.SubjectType),
            Clean(decisionEvent.ActorType),
            Clean(decisionEvent.ActorId),
            Clean(decisionEvent.Tenant),
            decisionEvent.DeterminingRule,
            string.Join(" | ", decisionEvent.PolicyReferences),
            Clean(decisionEvent.Narrative),
            decisionEvent.TraceId,
            decisionEvent.TimestampUtc);

    // Strip CR/LF from request-derived values before they reach the rendered log line, so an
    // untrusted subject/actor id or type (the /evaluate body is anonymous, caller-controlled)
    // cannot inject newlines to forge a fake log entry (CWE-117 log injection). Only the
    // human-readable log string is sanitized; the audit-of-record (PdpDecisionAuditEvent ->
    // Audit.Service hash chain) keeps the raw values.
    private static string? Clean(string? value) =>
        value?.Replace('\r', ' ').Replace('\n', ' ');
}
