namespace AuthzEntitlements.Entitlements.Service.Domain;

// A commercial plan tier definition: its seat allowance plus the modules and quotas
// it licenses. Modules and quotas are persisted rows (queryable per tenant); feature
// policy is code (see FeatureCatalog) because it drives OpenFeature evaluation.
public sealed class Plan
{
    public PlanTier Tier { get; set; }

    // Maximum seats the plan allows. EntitlementCatalog.Unlimited (-1) means no cap.
    public int SeatLimit { get; set; }

    public ICollection<PlanModule> Modules { get; } = [];
    public ICollection<PlanQuota> Quotas { get; } = [];
}
