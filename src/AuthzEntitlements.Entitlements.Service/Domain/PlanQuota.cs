namespace AuthzEntitlements.Entitlements.Service.Domain;

// A quota granted by a plan tier (e.g. monthly-transactions). Limit is the cap for
// the period; EntitlementCatalog.Unlimited (-1) means the quota is never denied.
public sealed class PlanQuota
{
    public PlanTier PlanTier { get; set; }
    public string QuotaKey { get; set; } = string.Empty;
    public long Limit { get; set; }

    public Plan Plan { get; set; } = null!;
}
