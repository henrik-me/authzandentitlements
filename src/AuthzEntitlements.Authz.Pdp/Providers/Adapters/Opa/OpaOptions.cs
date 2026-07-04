namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters.Opa;

// Configuration for the out-of-process OPA (Open Policy Agent) decision engine. Bound from
// the "Opa" section of configuration and consumed by both the named HttpClient (base address
// + timeout) and OpaDecisionProvider (decision path). Selecting the adapter is separate: set
// "Pdp:Provider" to "opa"; the default provider stays "reference" so builds/tests/`aspire run`
// never require a live OPA.
public sealed class OpaOptions
{
    public const string SectionName = "Opa";

    // Base address of the OPA REST server (the container's data API root). The decision request
    // is POSTed to BaseUrl + DecisionPath.
    public string BaseUrl { get; set; } = "http://localhost:8181";

    // Path (relative to BaseUrl) of the total decision rule the Rego policy exposes. Matches the
    // package/rule the parallel policy authors ship: data.authz.bank.decision.
    public string DecisionPath { get; set; } = "v1/data/authz/bank/decision";

    // Per-request HTTP timeout in seconds. A timeout fails closed (Deny/ProviderUnavailable),
    // never silently permits.
    public int TimeoutSeconds { get; set; } = 5;
}
