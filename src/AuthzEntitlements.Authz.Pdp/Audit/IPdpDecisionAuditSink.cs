namespace AuthzEntitlements.Authz.Pdp.Audit;

// Sink for audit-ready PDP decision events. CS05 only needs decisions *emitted* in an
// audit-ready shape; CS13's Audit.Service ingests them, so this interface is the seam
// that ingestion later replaces or augments.
public interface IPdpDecisionAuditSink
{
    void Record(PdpDecisionAuditEvent decisionEvent);
}
