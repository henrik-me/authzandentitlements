namespace AuthzEntitlements.Governance.Service.Domain;

// Pure planning + decision logic for access-review campaigns, kept free of EF so the
// item-generation and revoke-on-review rules are unit-testable without a live database.
// The endpoints persist whatever these methods produce or mutate; they add no other state.
public static class ReviewCampaignPlanner
{
    // Materialise one Pending review item per currently-active grant in the campaign's
    // tenant. Grants from other tenants and expired/revoked grants are excluded — a review
    // recertifies only the access that is actually in effect right now (IsActive).
    public static List<AccessReviewItem> BuildItems(
        AccessReviewCampaign campaign, IEnumerable<AccessGrant> grants, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        ArgumentNullException.ThrowIfNull(grants);

        return grants
            .Where(g => string.Equals(g.TenantCode, campaign.TenantCode, StringComparison.Ordinal)
                        && g.IsActive(now))
            .OrderBy(g => g.PrincipalId, StringComparer.Ordinal)
            .ThenBy(g => g.Id)
            .Select(g => new AccessReviewItem
            {
                Id = Guid.NewGuid(),
                CampaignId = campaign.Id,
                AccessGrantId = g.Id,
                PrincipalId = g.PrincipalId,
                Decision = ReviewDecision.Pending,
            })
            .ToList();
    }

    // Apply a reviewer's decision to an item. A Revoke immediately revokes the linked grant
    // (when supplied and not already revoked) so the principal loses the access at read
    // time; Certify keeps the grant. Returns true iff this call revoked the grant, so the
    // caller can emit the grant-revoked audit event/metric exactly once.
    public static bool ApplyDecision(
        AccessReviewItem item,
        ReviewDecision decision,
        string reviewedBy,
        AccessGrant? linkedGrant,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);

        item.Decision = decision;
        item.ReviewedBy = reviewedBy;
        item.ReviewedAt = now;

        if (decision == ReviewDecision.Revoke && linkedGrant is { RevokedAt: null })
        {
            linkedGrant.RevokedAt = now;
            linkedGrant.RevokedBy = reviewedBy;
            return true;
        }

        return false;
    }
}
