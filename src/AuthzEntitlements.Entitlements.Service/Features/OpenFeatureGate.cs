using AuthzEntitlements.Entitlements.Service.Domain;
using OpenFeature;
using OpenFeature.Model;

namespace AuthzEntitlements.Entitlements.Service.Features;

// Feature gate that evaluates through the OpenFeature client. It carries the tenant's
// plan tier in the evaluation context under "planTier"; the configured provider
// (in-memory catalog or Unleash) decides the boolean from there. Unknown flags fail
// closed via the false default.
public sealed class OpenFeatureGate : IFeatureGate
{
    private readonly FeatureClient _client = Api.Instance.GetClient();

    public async Task<bool> IsEnabledAsync(string featureKey, PlanTier tier, CancellationToken ct = default)
    {
        var context = EvaluationContext.Builder()
            .Set(FeatureContext.PlanTierKey, tier.ToString())
            .Build();

        return await _client.GetBooleanValueAsync(featureKey, false, context, cancellationToken: ct);
    }
}

// The evaluation-context keys shared between the gate and the in-memory provider's
// per-flag context evaluator, so both read/write the same attribute name.
public static class FeatureContext
{
    public const string PlanTierKey = "planTier";
}
