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
            Clean(decision.TenantCode),
            GovernanceWire.Token(decision.DecisionType),
            Clean(decision.Target),
            GovernanceWire.Token(decision.Outcome),
            Clean(decision.PrincipalId),
            Clean(decision.Reason),
            Clean(decision.CorrelationId),
            decision.TimestampUtc);

    // Strip CR/LF from every request-derived string before it reaches the ILogger message template, so
    // an untrusted tenant/principal/target/reason/correlation value can never inject a newline to forge
    // a fake audit log line (CWE-117 log injection) — mirrors LoggingPdpDecisionAuditSink.Clean. The
    // decision-type/outcome tokens are bounded enum values, so they need no cleaning. Only the rendered
    // log string is sanitized; the GovernanceDecision audit-of-record keeps the raw values.
    private static string? Clean(string? value) =>
        value?.Replace('\r', ' ').Replace('\n', ' ');

    [LoggerMessage(
        EventId = 1100,
        EventName = "GovernanceDecision",
        Level = LogLevel.Information,
        Message = "GovernanceDecision tenant={TenantCode} type={DecisionType} target={Target} " +
                  "outcome={Outcome} principal={PrincipalId} reason={Reason} " +
                  "correlationId={CorrelationId} at={TimestampUtc}")]
    private static partial void GovernanceDecisionLogged(
        ILogger logger,
        string? tenantCode,
        string decisionType,
        string? target,
        string outcome,
        string? principalId,
        string? reason,
        string? correlationId,
        DateTimeOffset timestampUtc);
}
