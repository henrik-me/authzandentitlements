using System.Diagnostics.Metrics;

namespace AuthzEntitlements.Governance.Service.Metering;

// Owns the OpenTelemetry Meter for governance decisions. Registered as a singleton and
// added to the OTel metrics pipeline (AddMeter) after AddServiceDefaults. Counters carry
// low-cardinality tags only (decision type, outcome) so the metric stream stays bounded.
public sealed class GovernanceMetrics : IDisposable
{
    public const string MeterName = "AuthzEntitlements.Governance";

    private readonly Meter _meter;
    private readonly Counter<long> _requests;
    private readonly Counter<long> _decisions;
    private readonly Counter<long> _grantsIssued;
    private readonly Counter<long> _grantsRevoked;
    private readonly Counter<long> _reviewsRun;

    public GovernanceMetrics()
    {
        _meter = new Meter(MeterName);
        _requests = _meter.CreateCounter<long>("governance.requests");
        _decisions = _meter.CreateCounter<long>("governance.decisions");
        _grantsIssued = _meter.CreateCounter<long>("governance.grants.issued");
        _grantsRevoked = _meter.CreateCounter<long>("governance.grants.revoked");
        _reviewsRun = _meter.CreateCounter<long>("governance.reviews.run");
    }

    public void RecordRequest() => _requests.Add(1);

    public void RecordDecision(string decisionType, string outcome) =>
        _decisions.Add(1,
            new KeyValuePair<string, object?>("type", decisionType),
            new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordGrantIssued() => _grantsIssued.Add(1);

    public void RecordGrantRevoked() => _grantsRevoked.Add(1);

    public void RecordReviewRun() => _reviewsRun.Add(1);

    public void Dispose() => _meter.Dispose();
}
