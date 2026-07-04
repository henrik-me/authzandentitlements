using System.Diagnostics;
using AuthzEntitlements.Authz.Pdp.Audit;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Telemetry;

namespace AuthzEntitlements.Authz.Pdp.Services;

// Thin orchestration around the selected provider so the audit + OTel hooks always fire:
// start a decision span, invoke the provider, tag + count the outcome, and emit exactly
// one audit event per decision. Endpoints and the scenario self-check call THIS, never a
// provider directly, so no decision escapes the hooks. The active provider is resolved
// once (fail closed) via the factory so a misconfigured "Pdp:Provider" fails at startup.
public sealed class PdpDecisionService
{
    private readonly IAuthorizationDecisionProvider _provider;
    private readonly IPdpDecisionAuditSink _audit;

    public PdpDecisionService(
        AuthorizationDecisionProviderFactory factory,
        IPdpDecisionAuditSink audit)
    {
        _provider = factory.GetActiveProvider();
        _audit = audit;
    }

    public string ProviderName => _provider.Name;

    public AccessDecision Evaluate(AccessRequest request)
    {
        using var activity = PdpTelemetry.StartDecisionActivity(_provider.Name, request.Action.Name);

        var decision = _provider.Evaluate(request);

        // Every decision the service returns must be explainable (CS16): use the provider's richer,
        // engine-native explanation when present, otherwise attach the shared baseline derived from
        // the primary reason so audit/telemetry and the returned decision always carry a "why".
        var explained = decision.Explanation is not null
            ? decision
            : decision.WithExplanation(DecisionExplanations.Baseline(_provider.Name, decision));

        // Reasons[0] is the contractual primary reason. If a provider violates the contract and
        // returns no reason, fall back to the decision's own name rather than a hardcoded
        // "Permit" — a reasonless Deny must never be mislabelled as a permit in audit/telemetry.
        var reasonCode = explained.Reasons.Count > 0
            ? explained.Reasons[0].Code
            : explained.Decision.ToString();
        var decisionName = explained.Decision.ToString();

        activity?.SetTag("pdp.decision", decisionName);
        activity?.SetTag("pdp.reason", reasonCode);

        // Metric tags must stay low-cardinality: action.name is caller-supplied and may be an
        // arbitrary/unknown verb, so normalize it to the bounded vocabulary for the counter. The
        // raw action stays on the span (above) and the audit event (below) for debugging.
        PdpTelemetry.RecordDecision(
            _provider.Name, ActionNames.ForMetric(request.Action.Name), decisionName, reasonCode);

        _audit.Record(new PdpDecisionAuditEvent(
            TimestampUtc: DateTimeOffset.UtcNow,
            // Prefer the decision span's trace id so the audit event and the pdp.evaluate span
            // correlate; fall back to the ambient activity, then empty when nothing is sampling.
            TraceId: (activity?.TraceId ?? Activity.Current?.TraceId)?.ToString() ?? string.Empty,
            Provider: _provider.Name,
            SubjectId: request.Subject.Id,
            Action: request.Action.Name,
            ResourceType: request.Resource.Type,
            ResourceId: request.Resource.Id,
            Decision: decisionName,
            Reason: reasonCode,
            Tenant: request.Subject.Tenant,
            DeterminingRule: explained.Explanation!.DeterminingRule,
            PolicyReferences: explained.Explanation!.PolicyReferences
                .Select(p => $"{p.Kind}:{p.Reference}").ToArray(),
            Narrative: explained.Explanation!.Narrative));

        return explained;
    }
}
