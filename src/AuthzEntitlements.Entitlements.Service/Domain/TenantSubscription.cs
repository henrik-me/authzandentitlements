namespace AuthzEntitlements.Entitlements.Service.Domain;

// A tenant's active plan subscription. Keyed on the tenant Code (the stable string
// used across the system) but also carries the tenant's Guid so later CSs can join
// on either. The plan tier drives every entitlement decision for the tenant.
public sealed class TenantSubscription
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string TenantCode { get; set; } = string.Empty;
    public PlanTier PlanTier { get; set; }

    public ICollection<SeatAssignment> Seats { get; } = [];
}
