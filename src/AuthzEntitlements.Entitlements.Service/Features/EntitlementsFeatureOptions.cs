namespace AuthzEntitlements.Entitlements.Service.Features;

// Which feature-flag backend the service uses. Bound from the "Entitlements" config
// section. InMemory is the default so build, tests, and `aspire run` are deterministic
// and never require the Unleash container.
public enum FeatureProviderKind
{
    InMemory,
    Unleash,
}

public sealed class EntitlementsFeatureOptions
{
    public const string SectionName = "Entitlements";

    public FeatureProviderKind FeatureProvider { get; set; } = FeatureProviderKind.InMemory;

    public UnleashOptions Unleash { get; set; } = new();
}

public sealed class UnleashOptions
{
    public string? Url { get; set; }
    public string? ApiToken { get; set; }
    public string AppName { get; set; } = "authz-entitlements";
}
