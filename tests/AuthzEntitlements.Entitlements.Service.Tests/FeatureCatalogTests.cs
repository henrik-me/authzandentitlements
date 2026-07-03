using AuthzEntitlements.Entitlements.Service.Domain;
using Xunit;

namespace AuthzEntitlements.Entitlements.Service.Tests;

// The FeatureCatalog is the single source of feature policy. These tests pin the exact
// tier-to-feature mapping from the CS10 contract so a policy change is a deliberate,
// test-breaking act.
public sealed class FeatureCatalogTests
{
    [Theory]
    [InlineData(PlanTier.Standard, false)]
    [InlineData(PlanTier.Professional, true)]
    [InlineData(PlanTier.Enterprise, true)]
    public void HighValueTransactions_EnabledFor_ProfessionalAndEnterprise(PlanTier tier, bool expected) =>
        Assert.Equal(expected, FeatureCatalog.IsEnabled(EntitlementCatalog.Features.HighValueTransactions, tier));

    [Theory]
    [InlineData(PlanTier.Standard, false)]
    [InlineData(PlanTier.Professional, false)]
    [InlineData(PlanTier.Enterprise, true)]
    public void BulkPayments_EnabledFor_EnterpriseOnly(PlanTier tier, bool expected) =>
        Assert.Equal(expected, FeatureCatalog.IsEnabled(EntitlementCatalog.Features.BulkPayments, tier));

    [Fact]
    public void FeaturesFor_Standard_IsEmpty() =>
        Assert.Empty(FeatureCatalog.FeaturesFor(PlanTier.Standard));

    [Fact]
    public void FeaturesFor_Professional_IsHighValueTransactionsOnly() =>
        Assert.Equal(
            new[] { EntitlementCatalog.Features.HighValueTransactions },
            FeatureCatalog.FeaturesFor(PlanTier.Professional));

    [Fact]
    public void FeaturesFor_Enterprise_IsBothFeatures() =>
        Assert.Equal(
            new[] { EntitlementCatalog.Features.HighValueTransactions, EntitlementCatalog.Features.BulkPayments },
            FeatureCatalog.FeaturesFor(PlanTier.Enterprise));

    [Fact]
    public void IsEnabled_UnknownFeature_FailsClosed() =>
        Assert.False(FeatureCatalog.IsEnabled("no-such-feature", PlanTier.Enterprise));

    [Fact]
    public void IsKnown_DistinguishesCatalogedFromUnknown()
    {
        Assert.True(FeatureCatalog.IsKnown(EntitlementCatalog.Features.BulkPayments));
        Assert.False(FeatureCatalog.IsKnown("no-such-feature"));
    }
}
