using System.Diagnostics.Metrics;

namespace AuthzEntitlements.Edge.Gateway.Telemetry;

// Emits the gateway's coarse-decision counter. Registered as a singleton so the
// Meter is created once from the shared IMeterFactory; the audit middleware
// records one decision per proxied /api request with decision/reason/route tags.
public sealed class GatewayMetrics
{
    private readonly Counter<long> _decisions;

    public GatewayMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(GatewayTelemetry.Name);
        _decisions = meter.CreateCounter<long>(
            "gateway.decisions",
            unit: "1",
            description: "Count of coarse edge authorization decisions.");
    }

    public void RecordDecision(string decision, string reason, string? routeId)
    {
        _decisions.Add(
            1,
            new KeyValuePair<string, object?>("decision", decision),
            new KeyValuePair<string, object?>("reason", reason),
            new KeyValuePair<string, object?>("route", routeId));
    }
}
