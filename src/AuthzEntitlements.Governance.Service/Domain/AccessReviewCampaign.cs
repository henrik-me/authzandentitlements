namespace AuthzEntitlements.Governance.Service.Domain;

// A recertification campaign: a bounded review of the active grants in a tenant. Running
// the campaign materialises one AccessReviewItem per active grant; the campaign closes
// once every item has a decision.
public sealed class AccessReviewCampaign
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset DueAt { get; set; }

    public CampaignStatus Status { get; set; } = CampaignStatus.Open;

    public ICollection<AccessReviewItem> Items { get; } = [];
}
