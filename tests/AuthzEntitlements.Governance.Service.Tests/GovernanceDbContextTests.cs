using AuthzEntitlements.Governance.Service.Data;
using AuthzEntitlements.Governance.Service.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AuthzEntitlements.Governance.Service.Tests;

// Model-metadata assertions over GovernanceDbContext. Building the model (and reading it) is
// offline — no database connection is opened — so these run without a live Postgres. The
// DB-level enforcement of the unique index itself is exercised by the migration, not here.
public sealed class GovernanceDbContextTests
{
    [Fact]
    public void AccessReviewItem_DeclaresUniqueIndex_OnCampaignIdThenAccessGrantId()
    {
        using var db = BuildContext();

        var entityType = db.Model.FindEntityType(typeof(AccessReviewItem));
        Assert.NotNull(entityType);

        // The composite {CampaignId, AccessGrantId} unique index is what stops two concurrent
        // campaign runs from both inserting review items for the same grant. CampaignId leads,
        // so the FK-only index is redundant and suppressed — hence this is the single index.
        var index = Assert.Single(entityType.GetIndexes());
        Assert.True(index.IsUnique);
        Assert.Equal(
            ["CampaignId", "AccessGrantId"],
            index.Properties.Select(p => p.Name));
    }

    private static GovernanceDbContext BuildContext()
    {
        var options = new DbContextOptionsBuilder<GovernanceDbContext>()
            .UseNpgsql("Host=localhost;Database=governance-model-metadata")
            .Options;
        return new GovernanceDbContext(options);
    }
}
