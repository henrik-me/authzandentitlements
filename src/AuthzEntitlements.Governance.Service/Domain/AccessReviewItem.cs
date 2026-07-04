namespace AuthzEntitlements.Governance.Service.Domain;

// A per-grant review row within a campaign. A reviewer certifies (keep the grant) or
// revokes (which revokes the underlying AccessGrant). Child of AccessReviewCampaign
// (cascade-deleted with it); AccessGrantId is a plain reference to the reviewed grant.
public sealed class AccessReviewItem
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public Guid AccessGrantId { get; set; }
    public string PrincipalId { get; set; } = string.Empty;

    public ReviewDecision Decision { get; set; } = ReviewDecision.Pending;
    public string? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
}
