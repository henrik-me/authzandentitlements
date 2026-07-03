namespace AuthzEntitlements.Authz.Pdp.Providers;

// Config-bound selection of the active PDP engine. Bound from the "Pdp" section; the
// deterministic in-process reference engine is the default so builds, tests, and
// `aspire run` never depend on an external engine. CS06-CS09 select their adapter by
// setting "Pdp:Provider" to the adapter's registered name.
public sealed class PdpOptions
{
    public const string SectionName = "Pdp";

    // The default engine name — the deterministic in-process reference provider. Centralized so
    // the factory can fall back to it when configuration leaves the provider name blank.
    public const string DefaultProvider = "reference";

    public string Provider { get; set; } = DefaultProvider;
}
