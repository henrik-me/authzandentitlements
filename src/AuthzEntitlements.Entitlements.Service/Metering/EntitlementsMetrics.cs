using System.Diagnostics.Metrics;

namespace AuthzEntitlements.Entitlements.Service.Metering;

// Owns the OpenTelemetry Meter for entitlement decisions. Registered as a singleton
// and added to the OTel metrics pipeline (AddMeter) after AddServiceDefaults. Counters
// carry low-cardinality tags only (decision type, outcome, quota key) so the metric
// stream stays bounded.
public sealed class EntitlementsMetrics : IDisposable
{
    public const string MeterName = "AuthzEntitlements.Entitlements";

    private readonly Meter _meter;
    private readonly Counter<long> _decisions;
    private readonly Counter<long> _quotaConsumed;

    public EntitlementsMetrics()
    {
        _meter = new Meter(MeterName);
        _decisions = _meter.CreateCounter<long>("entitlements.decisions");
        _quotaConsumed = _meter.CreateCounter<long>("entitlements.quota.consumed");
    }

    public void RecordDecision(string decisionType, string outcome) =>
        _decisions.Add(1,
            new KeyValuePair<string, object?>("type", decisionType),
            new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordQuotaConsumed(string quotaKey, long amount) =>
        _quotaConsumed.Add(amount, new KeyValuePair<string, object?>("key", quotaKey));

    public void Dispose() => _meter.Dispose();
}
