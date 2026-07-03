namespace AuthzEntitlements.Entitlements.Service.Domain;

// A single occupied seat on a subscription (one user consuming one seat). Seats-used
// is the count of these rows for a subscription; the seat limit lives on the plan.
public sealed class SeatAssignment
{
    public Guid Id { get; set; }
    public Guid SubscriptionId { get; set; }
    public Guid UserId { get; set; }

    public TenantSubscription Subscription { get; set; } = null!;
}
