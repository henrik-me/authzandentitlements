using AuthzEntitlements.Entitlements.Service.Domain;
using Microsoft.EntityFrameworkCore;

namespace AuthzEntitlements.Entitlements.Service.Data;

// Deterministic, idempotent seed for the commercial entitlements catalog. Guards on
// Plans.AnyAsync so re-running against an existing database is a no-op. The tenant
// ids/codes match the CS02 Bank seed exactly so cross-service joins line up.
public static class EntitlementsSeeder
{
    // Tenants (must match the CS02 Bank seed).
    public static readonly Guid ContosoTenantId = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid FabrikamTenantId = new("22222222-2222-2222-2222-222222222222");
    public const string ContosoCode = "CONTOSO";
    public const string FabrikamCode = "FABRIKAM";

    // Subscriptions (fixed so later PDP/audit scenarios can reference known ids).
    public static readonly Guid ContosoSubscriptionId = new("a0000000-0000-0000-0000-000000000001");
    public static readonly Guid FabrikamSubscriptionId = new("a0000000-0000-0000-0000-000000000002");

    public static async Task SeedAsync(EntitlementsDbContext db, CancellationToken ct = default)
    {
        if (await db.Plans.AnyAsync(ct))
        {
            return;
        }

        db.Plans.AddRange(BuildPlans());
        db.Subscriptions.AddRange(BuildSubscriptions());

        await db.SaveChangesAsync(ct);
    }

    // The plan catalog as a pure object graph (no persistence), so the tier -> modules
    // -> quotas composition is unit-testable without a database and the seeder has a
    // single definition of the three tiers.
    public static IReadOnlyList<Plan> BuildPlans()
    {
        var standard = new Plan { Tier = PlanTier.Standard, SeatLimit = 5 };
        standard.Quotas.Add(new PlanQuota
        {
            PlanTier = PlanTier.Standard, QuotaKey = EntitlementCatalog.Quotas.MonthlyTransactions, Limit = 100,
        });

        var professional = new Plan { Tier = PlanTier.Professional, SeatLimit = 25 };
        professional.Modules.Add(new PlanModule
        {
            PlanTier = PlanTier.Professional, ModuleKey = EntitlementCatalog.Modules.Wire,
        });
        professional.Modules.Add(new PlanModule
        {
            PlanTier = PlanTier.Professional, ModuleKey = EntitlementCatalog.Modules.Fx,
        });
        professional.Quotas.Add(new PlanQuota
        {
            PlanTier = PlanTier.Professional, QuotaKey = EntitlementCatalog.Quotas.MonthlyTransactions, Limit = 1000,
        });

        var enterprise = new Plan { Tier = PlanTier.Enterprise, SeatLimit = (int)EntitlementCatalog.Unlimited };
        enterprise.Modules.Add(new PlanModule
        {
            PlanTier = PlanTier.Enterprise, ModuleKey = EntitlementCatalog.Modules.Wire,
        });
        enterprise.Modules.Add(new PlanModule
        {
            PlanTier = PlanTier.Enterprise, ModuleKey = EntitlementCatalog.Modules.Fx,
        });
        enterprise.Modules.Add(new PlanModule
        {
            PlanTier = PlanTier.Enterprise, ModuleKey = EntitlementCatalog.Modules.Treasury,
        });
        enterprise.Quotas.Add(new PlanQuota
        {
            PlanTier = PlanTier.Enterprise,
            QuotaKey = EntitlementCatalog.Quotas.MonthlyTransactions,
            Limit = EntitlementCatalog.Unlimited,
        });

        return [standard, professional, enterprise];
    }

    public static IReadOnlyList<TenantSubscription> BuildSubscriptions() =>
    [
        new TenantSubscription
        {
            Id = ContosoSubscriptionId,
            TenantId = ContosoTenantId,
            TenantCode = ContosoCode,
            PlanTier = PlanTier.Professional,
        },
        new TenantSubscription
        {
            Id = FabrikamSubscriptionId,
            TenantId = FabrikamTenantId,
            TenantCode = FabrikamCode,
            PlanTier = PlanTier.Standard,
        },
    ];
}
