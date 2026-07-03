namespace AuthzEntitlements.Entitlements.Service.Domain;

// The single source of truth for feature-gate policy: which plan tiers enable which
// feature. Both the OpenFeature in-memory provider (per-flag context evaluator) and
// the /plan summary consume this same map, so feature policy lives in exactly one
// place and can never drift between "is this feature on?" and "what does the plan
// advertise?".
public static class FeatureCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<PlanTier>> Policy =
        new Dictionary<string, IReadOnlySet<PlanTier>>(StringComparer.Ordinal)
        {
            [EntitlementCatalog.Features.HighValueTransfers] =
                new HashSet<PlanTier> { PlanTier.Professional, PlanTier.Enterprise },
            [EntitlementCatalog.Features.BulkPayments] =
                new HashSet<PlanTier> { PlanTier.Enterprise },
        };

    // Every feature key the catalog knows about, in stable declaration order.
    public static IReadOnlyList<string> Keys { get; } =
        [EntitlementCatalog.Features.HighValueTransfers, EntitlementCatalog.Features.BulkPayments];

    public static bool IsKnown(string featureKey) => Policy.ContainsKey(featureKey);

    // True when the given tier enables the feature. Unknown feature keys fail closed
    // (disabled), matching the OpenFeature default-value behaviour.
    public static bool IsEnabled(string featureKey, PlanTier tier) =>
        Policy.TryGetValue(featureKey, out var tiers) && tiers.Contains(tier);

    // The enabled feature keys for a tier, ordered stably for the /plan summary.
    public static IReadOnlyList<string> FeaturesFor(PlanTier tier) =>
        Keys.Where(k => IsEnabled(k, tier)).ToList();
}
