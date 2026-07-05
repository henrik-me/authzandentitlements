namespace AuthzEntitlements.Governance.Service.Metering;

// Single source of truth for emitting a governance decision: record the audit-ready event and
// bump the low-cardinality decision counter. Extracted from the two endpoint modules
// (GovernanceEndpoints and BreakGlassDelegationEndpoints), which previously carried byte-identical
// private copies, so the emit shape can never drift between them.
internal static class GovernanceDecisionEmitter
{
    public static void Emit(
        IGovernanceAuditSink audit,
        GovernanceMetrics metrics,
        string tenantCode,
        string principalId,
        GovernanceDecisionType type,
        string target,
        GovernanceOutcome outcome,
        string? reason,
        string? correlationId)
    {
        audit.Record(new GovernanceDecision(
            tenantCode, principalId, type, target, outcome, reason, correlationId, DateTimeOffset.UtcNow));
        metrics.RecordDecision(GovernanceWire.Token(type), GovernanceWire.Token(outcome));
    }
}
