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

    // A campaign is run exactly once: RunCampaignAsync materialises one review item per active
    // grant, so a second run would regenerate duplicates. Returns true when the campaign already
    // has items and must not be re-run. The DB's unique {CampaignId, AccessGrantId} index is the
    // durable backstop for the concurrent-run race; this is the cheap in-memory fast-path guard.
    public static bool AlreadyRun(AccessReviewCampaign campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        return campaign.Items.Count > 0;
    }

    // Apply a reviewer's decision to an item. A Revoke immediately revokes the linked grant
    // (when not already revoked) so the principal loses the access at read time; Certify keeps
    // the grant. Returns true iff this call revoked the grant, so the caller can emit the
    // grant-revoked audit event/metric exactly once.
    //
    // A Revoke requires a real grant: the caller resolves the linked grant and rejects a missing
    // one (409) before calling here. Guarding the invariant — rather than silently marking the
    // item Revoked with no grant to revoke — stops the audit trail from ever claiming a
    // revocation that did not happen. A Certify does not touch a grant, so linkedGrant is null.
    public static bool ApplyDecision(
        AccessReviewItem item,
        ReviewDecision decision,
        string reviewedBy,
        AccessGrant? linkedGrant,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (decision == ReviewDecision.Revoke && linkedGrant is null)
        {
            throw new ArgumentException(
                "a Revoke decision requires the linked grant; resolve it before applying.",
                nameof(linkedGrant));
        }

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
