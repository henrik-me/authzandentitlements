using AuthzEntitlements.Entitlements.Service.Data;
using AuthzEntitlements.Entitlements.Service.Domain;
using Xunit;

namespace AuthzEntitlements.Entitlements.Service.Tests;

// Verifies the seed plan catalog composes correctly (tier -> seat limit -> modules ->
// quota) and that a /plan-style summary (DB modules + FeatureCatalog features) matches
// the CS10 contract per tier. Exercised against the pure object graph, no database.
public sealed class PlanCompositionTests
{
    private static Plan Plan(PlanTier tier) =>
        EntitlementsSeeder.BuildPlans().Single(p => p.Tier == tier);

    private static string[] ModulesOf(PlanTier tier) =>
        Plan(tier).Modules.Select(m => m.ModuleKey).OrderBy(k => k, StringComparer.Ordinal).ToArray();

    private static string[] FeaturesOf(PlanTier tier) =>
        FeatureCatalog.FeaturesFor(tier).ToArray();

    [Fact]
    public void Standard_HasNoModules_NoFeatures_SeatLimitFive_Quota100()
    {
        Assert.Empty(ModulesOf(PlanTier.Standard));
        Assert.Empty(FeaturesOf(PlanTier.Standard));
        Assert.Equal(5, Plan(PlanTier.Standard).SeatLimit);
        Assert.Equal(100, QuotaLimit(PlanTier.Standard));
    }

    [Fact]
    public void Professional_HasWireFx_HighValueTransfers_SeatLimit25_Quota1000()
    {
        Assert.Equal(
            new[] { EntitlementCatalog.Modules.Fx, EntitlementCatalog.Modules.Wire },
            ModulesOf(PlanTier.Professional));
        Assert.Equal(new[] { EntitlementCatalog.Features.HighValueTransfers }, FeaturesOf(PlanTier.Professional));
        Assert.Equal(25, Plan(PlanTier.Professional).SeatLimit);
        Assert.Equal(1000, QuotaLimit(PlanTier.Professional));
    }

    [Fact]
    public void Enterprise_HasAllModules_BothFeatures_UnlimitedSeats_UnlimitedQuota()
    {
        Assert.Equal(
            new[] { EntitlementCatalog.Modules.Fx, EntitlementCatalog.Modules.Treasury, EntitlementCatalog.Modules.Wire },
            ModulesOf(PlanTier.Enterprise));
        Assert.Equal(
            new[] { EntitlementCatalog.Features.HighValueTransfers, EntitlementCatalog.Features.BulkPayments },
            FeaturesOf(PlanTier.Enterprise));
        Assert.Equal((int)EntitlementCatalog.Unlimited, Plan(PlanTier.Enterprise).SeatLimit);
        Assert.Equal(EntitlementCatalog.Unlimited, QuotaLimit(PlanTier.Enterprise));
    }

    [Fact]
    public void Subscriptions_MatchContract()
    {
        var subs = EntitlementsSeeder.BuildSubscriptions();

        var contoso = subs.Single(s => s.TenantCode == EntitlementsSeeder.ContosoCode);
        Assert.Equal(PlanTier.Professional, contoso.PlanTier);
        Assert.Equal(EntitlementsSeeder.ContosoTenantId, contoso.TenantId);

        var fabrikam = subs.Single(s => s.TenantCode == EntitlementsSeeder.FabrikamCode);
        Assert.Equal(PlanTier.Standard, fabrikam.PlanTier);
        Assert.Equal(EntitlementsSeeder.FabrikamTenantId, fabrikam.TenantId);
    }

    private static long QuotaLimit(PlanTier tier) =>
        Plan(tier).Quotas.Single(q => q.QuotaKey == EntitlementCatalog.Quotas.MonthlyTransactions).Limit;
}
