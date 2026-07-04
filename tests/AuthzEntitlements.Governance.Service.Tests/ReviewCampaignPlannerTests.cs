using AuthzEntitlements.Governance.Service.Domain;
using Xunit;

namespace AuthzEntitlements.Governance.Service.Tests;

// Running a campaign materialises one review item per currently-active grant in the
// campaign's tenant, and a Revoke decision revokes the underlying grant (item g).
public sealed class ReviewCampaignPlannerTests
{
    private static readonly DateTimeOffset Now = GovernanceTestData.Now;

    [Fact]
    public void BuildItems_CreatesOnePendingItemPerActiveGrantInTenant()
    {
        var campaign = Campaign(GovernanceTestData.Contoso);
        var grants = new[]
        {
            ActiveGrant("user-teller1"),
            ActiveGrant("user-manager1"),
        };

        var items = ReviewCampaignPlanner.BuildItems(campaign, grants, Now);

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal(ReviewDecision.Pending, i.Decision));
        Assert.All(items, i => Assert.Equal(campaign.Id, i.CampaignId));
        Assert.Equal(
            ["user-manager1", "user-teller1"],
            items.Select(i => i.PrincipalId).OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void BuildItems_ExcludesGrantsFromOtherTenants()
    {
        var campaign = Campaign(GovernanceTestData.Contoso);
        var grants = new[]
        {
            ActiveGrant("user-teller1"),
            ActiveGrant("user-other", tenant: "FABRIKAM"),
        };

        var items = ReviewCampaignPlanner.BuildItems(campaign, grants, Now);

        var item = Assert.Single(items);
        Assert.Equal("user-teller1", item.PrincipalId);
    }

    [Fact]
    public void BuildItems_ExcludesExpiredAndRevokedGrants()
    {
        var campaign = Campaign(GovernanceTestData.Contoso);
        var grants = new[]
        {
            ActiveGrant("user-active"),
            GovernanceTestData.Grant("user-expired", GovernanceTestData.Contoso, "quarter-end-close",
                Now.AddMinutes(-600), Now.AddMinutes(-120), ["ComplianceOfficer"]),
            GovernanceTestData.Grant("user-revoked", GovernanceTestData.Contoso, "quarter-end-close",
                Now.AddMinutes(-10), Now.AddMinutes(470), ["ComplianceOfficer"], revokedAt: Now.AddMinutes(-1)),
        };

        var items = ReviewCampaignPlanner.BuildItems(campaign, grants, Now);

        var item = Assert.Single(items);
        Assert.Equal("user-active", item.PrincipalId);
    }

    [Fact]
    public void ApplyDecision_Revoke_RevokesLinkedGrant_AndReturnsTrue()
    {
        var grant = ActiveGrant("user-teller1");
        var item = PendingItem(grant);

        var revoked = ReviewCampaignPlanner.ApplyDecision(item, ReviewDecision.Revoke, "user-auditor1", grant, Now);

        Assert.True(revoked);
        Assert.Equal(Now, grant.RevokedAt);
        Assert.Equal("user-auditor1", grant.RevokedBy);
        Assert.False(grant.IsActive(Now));
        Assert.Equal(ReviewDecision.Revoke, item.Decision);
        Assert.Equal("user-auditor1", item.ReviewedBy);
        Assert.Equal(Now, item.ReviewedAt);
    }

    [Fact]
    public void ApplyDecision_Certify_LeavesGrantActive_AndReturnsFalse()
    {
        var grant = ActiveGrant("user-teller1");
        var item = PendingItem(grant);

        var revoked = ReviewCampaignPlanner.ApplyDecision(item, ReviewDecision.Certify, "user-auditor1", grant, Now);

        Assert.False(revoked);
        Assert.Null(grant.RevokedAt);
        Assert.True(grant.IsActive(Now));
        Assert.Equal(ReviewDecision.Certify, item.Decision);
    }

    [Fact]
    public void ApplyDecision_RevokeAlreadyRevokedGrant_ReturnsFalse_AndKeepsOriginalRevocation()
    {
        var revokedAt = Now.AddMinutes(-30);
        var grant = GovernanceTestData.Grant("user-teller1", GovernanceTestData.Contoso, "quarter-end-close",
            Now.AddMinutes(-60), Now.AddMinutes(420), ["ComplianceOfficer"], revokedAt);
        var item = PendingItem(grant);

        var revoked = ReviewCampaignPlanner.ApplyDecision(item, ReviewDecision.Revoke, "user-auditor1", grant, Now);

        // Already revoked: the item still records the decision, but the grant's original
        // revocation stamp is not overwritten and no new revocation event is signalled.
        Assert.False(revoked);
        Assert.Equal(revokedAt, grant.RevokedAt);
        Assert.Equal(ReviewDecision.Revoke, item.Decision);
    }

    private static AccessReviewCampaign Campaign(string tenant) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = "Q1 recertification",
            TenantCode = tenant,
            CreatedAt = Now,
            DueAt = Now.AddDays(14),
            Status = CampaignStatus.Open,
        };

    private static AccessGrant ActiveGrant(string principalId, string tenant = GovernanceTestData.Contoso) =>
        GovernanceTestData.Grant(
            principalId, tenant, "quarter-end-close",
            Now.AddMinutes(-10), Now.AddMinutes(470), ["ComplianceOfficer"]);

    private static AccessReviewItem PendingItem(AccessGrant grant) =>
        new()
        {
            Id = Guid.NewGuid(),
            CampaignId = Guid.NewGuid(),
            AccessGrantId = grant.Id,
            PrincipalId = grant.PrincipalId,
            Decision = ReviewDecision.Pending,
        };
}
