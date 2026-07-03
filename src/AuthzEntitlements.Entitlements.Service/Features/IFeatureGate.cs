using AuthzEntitlements.Entitlements.Service.Domain;

namespace AuthzEntitlements.Entitlements.Service.Features;

// Evaluates a feature flag for a tenant's plan tier. Backed by OpenFeature so the
// underlying provider (in-memory by default, Unleash when configured) is swappable
// without touching the endpoint.
public interface IFeatureGate
{
    Task<bool> IsEnabledAsync(string featureKey, PlanTier tier, CancellationToken ct = default);
}
