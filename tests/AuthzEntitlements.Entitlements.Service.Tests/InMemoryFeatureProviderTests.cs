using AuthzEntitlements.Entitlements.Service.Domain;
using AuthzEntitlements.Entitlements.Service.Features;
using OpenFeature.Model;
using Xunit;

namespace AuthzEntitlements.Entitlements.Service.Tests;

// Exercises the OpenFeature in-memory provider built from the FeatureCatalog. The
// provider is evaluated directly (no global Api state) so the tests are order- and
// parallel-safe, and prove the per-context evaluator returns the catalog result for a
// given planTier.
public sealed class InMemoryFeatureProviderTests
{
    private static EvaluationContext ContextFor(PlanTier tier) =>
        EvaluationContext.Builder().Set(FeatureContext.PlanTierKey, tier.ToString()).Build();

    [Theory]
    [InlineData(PlanTier.Standard, false)]
    [InlineData(PlanTier.Professional, true)]
    [InlineData(PlanTier.Enterprise, true)]
    public async Task HighValueTransactions_ResolvesFromCatalog(PlanTier tier, bool expected)
    {
        var provider = InMemoryFeatureProviderFactory.Create();

        var result = await provider.ResolveBooleanValueAsync(
            EntitlementCatalog.Features.HighValueTransactions, false, ContextFor(tier), CancellationToken.None);

        Assert.Equal(expected, result.Value);
    }

    [Theory]
    [InlineData(PlanTier.Standard, false)]
    [InlineData(PlanTier.Professional, false)]
    [InlineData(PlanTier.Enterprise, true)]
    public async Task BulkPayments_ResolvesFromCatalog(PlanTier tier, bool expected)
    {
        var provider = InMemoryFeatureProviderFactory.Create();

        var result = await provider.ResolveBooleanValueAsync(
            EntitlementCatalog.Features.BulkPayments, false, ContextFor(tier), CancellationToken.None);

        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public async Task UnknownFlag_ReturnsDefaultFalse()
    {
        var provider = InMemoryFeatureProviderFactory.Create();

        var result = await provider.ResolveBooleanValueAsync(
            "no-such-feature", false, ContextFor(PlanTier.Enterprise), CancellationToken.None);

        Assert.False(result.Value);
    }

    [Fact]
    public async Task MissingPlanTierContext_FailsClosed()
    {
        var provider = InMemoryFeatureProviderFactory.Create();

        var result = await provider.ResolveBooleanValueAsync(
            EntitlementCatalog.Features.HighValueTransactions, false,
            EvaluationContext.Empty, CancellationToken.None);

        Assert.False(result.Value);
    }
}
