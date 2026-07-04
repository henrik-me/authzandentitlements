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
            "actor={ActorType}/{ActorId} tenant={Tenant} " +
            "rule={DeterminingRule} policyReferences={PolicyReferences} narrative={Narrative} " +
            "trace={TraceId} at {TimestampUtc}",
            decisionEvent.Decision,
            decisionEvent.Reason,
            decisionEvent.Provider,
            decisionEvent.Action,
            decisionEvent.ResourceType,
            decisionEvent.ResourceId,
            decisionEvent.SubjectId,
            decisionEvent.SubjectType,
            decisionEvent.ActorType,
            decisionEvent.ActorId,
            decisionEvent.Tenant,
            decisionEvent.DeterminingRule,
            string.Join(" | ", decisionEvent.PolicyReferences),
            decisionEvent.Narrative,
            decisionEvent.TraceId,
            decisionEvent.TimestampUtc);
}
