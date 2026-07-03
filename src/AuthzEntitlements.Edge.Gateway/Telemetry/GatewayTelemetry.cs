using System.Diagnostics;

namespace AuthzEntitlements.Edge.Gateway.Telemetry;

// Shared OpenTelemetry names + decision/reason vocabulary for the gateway. The
// ActivitySource and Meter share a single Name so the AppHost/collector wires one
// source. These constants keep tag values consistent across traces, metrics, and
// the structured audit event (CS13 ingests the same decision/reason vocabulary).
public static class GatewayTelemetry
{
    public const string Name = "AuthzEntitlements.Edge.Gateway";

    public static readonly ActivitySource ActivitySource = new(Name);

    // Coarse decision outcomes.
    public const string DecisionAllow = "allow";
    public const string DecisionDeny = "deny";

    // Why the gateway allowed/denied — the audit-ready reason vocabulary.
    public const string ReasonRouted = "routed";
    public const string ReasonUnauthenticated = "unauthenticated";
    public const string ReasonMissingTenant = "missing-tenant";
    public const string ReasonMissingScope = "missing-scope";
}
