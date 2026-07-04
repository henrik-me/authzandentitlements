using AuthzEntitlements.Bank.Web.Clients;

namespace AuthzEntitlements.Bank.Web.ViewModels;

// Pure, dependency-free helpers for the commercial ENTITLEMENTS / feature-gate page. Kept
// out of the .razor so the demo-key list, gate labelling, and the fail-closed response
// mapping are unit-testable offline (no server, Docker, or Keycloak). Fail-closed: a null
// response (service unreachable or unknown key) maps to a DISABLED gate with a clear
// reason — the local service catalog is the source of truth, never a client-side default.
public static class EntitlementsModel
{
    // The demo feature keys surfaced on the page, in stable display order. Mirrors the
    // Entitlements.Service FeatureCatalog keys; this copy only drives the UI — the service
    // is the authority on whether a key is enabled for a tenant's plan tier.
    public static readonly IReadOnlyList<string> DemoFeatureKeys =
        ["high-value-transactions", "bulk-payments"];

    // Human-readable label for a feature gate. "Gated" makes the commercial-upgrade path
    // explicit rather than reading as a hard error.
    public static string GateLabel(bool enabled) =>
        enabled ? "Enabled" : "Gated (upgrade required)";

    // A resolved feature gate for one key: enabled?, the plan tier the service evaluated
    // against, and the service's reason string.
    public sealed record FeatureGateView(string Key, bool Enabled, string PlanTier, string Reason);

    // Maps a service response to a view. Fail-closed: a null response (unreachable service,
    // non-2xx, or unknown key surfaced as null) becomes a DISABLED gate with an explicit
    // reason, never a silent allow.
    public static FeatureGateView FromResponse(string key, FeatureEntitlementResponse? resp) =>
        resp is null
            ? new FeatureGateView(key, false, "unknown", "Entitlements unavailable — fail-closed.")
            : new FeatureGateView(key, resp.Enabled, resp.PlanTier, resp.Reason);
}
