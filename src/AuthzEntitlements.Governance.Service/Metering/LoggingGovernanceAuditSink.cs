using Microsoft.Extensions.Logging;

namespace AuthzEntitlements.Governance.Service.Metering;

// Sink for audit-ready governance decision events. CS11 requires the decisions to be
// *emitted* in an audit-ready shape; Audit.Service ingests them in CS13, so this
// interface is the seam that ingestion will later replace/augment.
public interface IGovernanceAuditSink
{
    void Record(GovernanceDecision decision);
}

// Default implementation: emit a structured ILogger event named "GovernanceDecision" with
// every decision field as a named property (decision-type and outcome lower-cased for
// stable matching), so a log/OTel pipeline captures a queryable audit trail without any
// external audit-service call in CS11.
public sealed partial class LoggingGovernanceAuditSink(ILogger<LoggingGovernanceAuditSink> logger)
    : IGovernanceAuditSink
{
    public void Record(GovernanceDecision decision) =>
        GovernanceDecisionLogged(
            logger,
            decision.TenantCode,
            GovernanceWire.Token(decision.DecisionType),
            decision.Target,
            GovernanceWire.Token(decision.Outcome),
            decision.PrincipalId,
            decision.Reason,
            decision.CorrelationId,
            decision.TimestampUtc);

    [LoggerMessage(
        EventId = 1100,
        EventName = "GovernanceDecision",
        Level = LogLevel.Information,
        Message = "GovernanceDecision tenant={TenantCode} type={DecisionType} target={Target} " +
                  "outcome={Outcome} principal={PrincipalId} reason={Reason} " +
                  "correlationId={CorrelationId} at={TimestampUtc}")]
    private static partial void GovernanceDecisionLogged(
        ILogger logger,
        string tenantCode,
        string decisionType,
        string target,
        string outcome,
        string principalId,
        string? reason,
        string? correlationId,
        DateTimeOffset timestampUtc);
}
