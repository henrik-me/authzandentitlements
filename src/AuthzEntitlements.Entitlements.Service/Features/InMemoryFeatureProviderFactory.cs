using AuthzEntitlements.Entitlements.Service.Domain;
using OpenFeature.Model;
using OpenFeature.Providers.Memory;

namespace AuthzEntitlements.Entitlements.Service.Features;

// Builds the OpenFeature in-memory provider from the single FeatureCatalog policy.
// Each feature key becomes a boolean flag whose per-context evaluator reads the
// "planTier" attribute and returns "on"/"off" straight from FeatureCatalog — so the
// in-memory provider and the /plan summary can never disagree about feature policy.
public static class InMemoryFeatureProviderFactory
{
    public static InMemoryProvider Create()
    {
        var flags = new Dictionary<string, Flag>(StringComparer.Ordinal);

        foreach (var key in FeatureCatalog.Keys)
        {
            var featureKey = key;
            flags[featureKey] = new Flag<bool>(
                new Dictionary<string, bool> { ["on"] = true, ["off"] = false },
                "off",
                context => ResolveVariant(featureKey, context));
        }

        return new InMemoryProvider(flags);
    }

    private static string ResolveVariant(string featureKey, EvaluationContext context)
    {
        if (context.TryGetValue(FeatureContext.PlanTierKey, out var value)
            && value is { IsString: true }
            && Enum.TryParse<PlanTier>(value.AsString, out var tier)
            && FeatureCatalog.IsEnabled(featureKey, tier))
        {
            return "on";
        }

        return "off";
    }
}
