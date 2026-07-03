using Microsoft.Extensions.Logging;

namespace AuthzEntitlements.Entitlements.Service.Metering;

// Sink for audit-ready entitlement decision events. CS10's exit criteria require the
// decisions to be *emitted* in an audit-ready shape; Audit.Service ingests them in
// CS13, so this interface is the seam that ingestion will later replace/augment.
public interface IEntitlementAuditSink
{
    void Record(EntitlementDecision decision);
}

// Default implementation: emit a structured ILogger event named "EntitlementDecision"
// with every decision field as a named property, so a log/OTel pipeline captures a
// queryable audit trail without any external audit service call in CS10.
public sealed partial class LoggingEntitlementAuditSink(ILogger<LoggingEntitlementAuditSink> logger)
    : IEntitlementAuditSink
{
    public void Record(EntitlementDecision decision) =>
        EntitlementDecisionLogged(
            logger,
            decision.TenantCode,
            decision.DecisionType.ToString(),
            decision.Key,
            decision.Outcome.ToString(),
            decision.PlanTier,
            decision.Amount,
            decision.Used,
            decision.Limit,
            decision.TimestampUtc);

    [LoggerMessage(
        EventId = 1000,
        EventName = "EntitlementDecision",
        Level = LogLevel.Information,
        Message = "EntitlementDecision tenant={TenantCode} type={DecisionType} key={Key} " +
                  "outcome={Outcome} plan={PlanTier} amount={Amount} used={Used} limit={Limit} at={TimestampUtc}")]
    private static partial void EntitlementDecisionLogged(
        ILogger logger,
        string tenantCode,
        string decisionType,
        string key,
        string outcome,
        string planTier,
        long? amount,
        long? used,
        long? limit,
        DateTimeOffset timestampUtc);
}
