using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AuthzEntitlements.Authz.Pdp.Telemetry;

// Shared OpenTelemetry primitives for the PDP. The ActivitySource and Meter share the
// single Name so the AppHost/collector wires one source. The evaluate path starts one
// span and increments one counter per decision, tagged with the low-cardinality
// provider/action/decision/reason vocabulary the audit event also carries.
public static class PdpTelemetry
{
    public const string Name = "AuthzEntitlements.Authz.Pdp";

    public static readonly ActivitySource ActivitySource = new(Name);

    public static readonly Meter Meter = new(Name);

    public static readonly Counter<long> DecisionsTotal =
        Meter.CreateCounter<long>("pdp.decisions.total");

    // Starts a decision span carrying the provider + action tags. Returns null when no
    // listener is sampling; callers null-condition the tag calls and dispose.
    public static Activity? StartDecisionActivity(string provider, string action)
    {
        var activity = ActivitySource.StartActivity("pdp.evaluate", ActivityKind.Internal);
        activity?.SetTag("pdp.provider", provider);
        activity?.SetTag("pdp.action", action);
        return activity;
    }

    public static void RecordDecision(string provider, string action, string decision, string reason) =>
        DecisionsTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("action", action),
            new KeyValuePair<string, object?>("decision", decision),
            new KeyValuePair<string, object?>("reason", reason));
}
