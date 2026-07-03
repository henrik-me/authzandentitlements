using OpenFeature;
using Unleash;

namespace AuthzEntitlements.Entitlements.Service.Features;

// Provider-selection seam. Chooses the OpenFeature provider from configuration:
// InMemory (default, deterministic, tested) or Unleash (config-gated, requires the
// running Unleash server; never on the default path). Kept lazy so nothing touches
// the network or throws when the Unleash section is absent — this also lets the model
// build at design time (dotnet ef migrations) without any provider being constructed.
public static class FeatureProviderFactory
{
    public static FeatureProvider Create(EntitlementsFeatureOptions options) =>
        options.FeatureProvider switch
        {
            FeatureProviderKind.InMemory => InMemoryFeatureProviderFactory.Create(),
            FeatureProviderKind.Unleash => CreateUnleash(options.Unleash),
            _ => InMemoryFeatureProviderFactory.Create(),
        };

    private static FeatureProvider CreateUnleash(UnleashOptions unleash)
    {
        if (string.IsNullOrWhiteSpace(unleash.Url))
        {
            throw new InvalidOperationException(
                "Entitlements:FeatureProvider is 'Unleash' but Entitlements:Unleash:Url is not configured. " +
                "Set the Unleash URL and ApiToken, or use the default 'InMemory' provider.");
        }

        var settings = new UnleashSettings
        {
            AppName = unleash.AppName,
            UnleashApi = new Uri(unleash.Url),
        };

        if (!string.IsNullOrWhiteSpace(unleash.ApiToken))
        {
            settings.CustomHttpHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = unleash.ApiToken,
            };
        }

        return new UnleashFeatureProvider(new DefaultUnleash(settings));
    }
}
