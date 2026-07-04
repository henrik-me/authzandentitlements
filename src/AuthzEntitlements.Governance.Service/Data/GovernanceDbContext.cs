using AuthzEntitlements.Governance.Service.Domain;
using Microsoft.EntityFrameworkCore;

namespace AuthzEntitlements.Governance.Service.Data;

// Owns the `governance` Postgres database (access packages, principals, JIT grant
// requests, time-bound grants, and access-review campaigns). Mirrors the Entitlements
// DbContext idioms: enums are stored as strings, lookup keys have unique indexes, child
// collections cascade-delete with their parent, and the decide-once request table carries
// the Postgres xmin system column as an optimistic-concurrency token.
public sealed class GovernanceDbContext(DbContextOptions<GovernanceDbContext> options)
    : DbContext(options)
{
    public DbSet<AccessPackage> AccessPackages => Set<AccessPackage>();
    public DbSet<Principal> Principals => Set<Principal>();
    public DbSet<AccessGrantRequest> AccessGrantRequests => Set<AccessGrantRequest>();
    public DbSet<AccessGrant> AccessGrants => Set<AccessGrant>();
    public DbSet<AccessReviewCampaign> AccessReviewCampaigns => Set<AccessReviewCampaign>();
    public DbSet<AccessReviewItem> AccessReviewItems => Set<AccessReviewItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AccessPackage>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Code).IsRequired().HasMaxLength(100);
            e.Property(p => p.DisplayName).IsRequired().HasMaxLength(200);
            e.Property(p => p.Description).IsRequired().HasMaxLength(1000);
            e.HasIndex(p => p.Code).IsUnique();
            e.HasMany(p => p.Roles)
                .WithOne()
                .HasForeignKey(r => r.AccessPackageId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AccessPackageRole>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.RoleName).IsRequired().HasMaxLength(50);
            e.HasIndex(r => new { r.AccessPackageId, r.RoleName }).IsUnique();
        });

        modelBuilder.Entity<Principal>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasMaxLength(100);
            e.Property(p => p.TenantCode).IsRequired().HasMaxLength(50);
            e.Property(p => p.DisplayName).IsRequired().HasMaxLength(200);
            e.HasMany(p => p.BaselineRoles)
                .WithOne()
                .HasForeignKey(r => r.PrincipalId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PrincipalRole>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.PrincipalId).IsRequired().HasMaxLength(100);
            e.Property(r => r.RoleName).IsRequired().HasMaxLength(50);
            e.HasIndex(r => new { r.PrincipalId, r.RoleName }).IsUnique();
        });

        modelBuilder.Entity<AccessGrantRequest>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.PrincipalId).IsRequired().HasMaxLength(100);
            e.Property(r => r.TenantCode).IsRequired().HasMaxLength(50);
            e.Property(r => r.AccessPackageCode).IsRequired().HasMaxLength(100);
            e.Property(r => r.Justification).IsRequired().HasMaxLength(1000);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(r => r.SodOutcome).HasConversion<string>().HasMaxLength(30);
            e.Property(r => r.SodReason).HasMaxLength(200);
            e.Property(r => r.DecidedBy).HasMaxLength(100);
            e.HasIndex(r => r.PrincipalId);
            e.HasIndex(r => r.Status);
            // Optimistic concurrency for approve/reject: two concurrent decisions on the
            // same request both read the same row and would otherwise last-writer-wins. The
            // xmin system-column token makes the losing SaveChanges throw a
            // DbUpdateConcurrencyException, so the endpoint surfaces a 409 instead of
            // silently double-deciding. Npgsql maps the shadow uint to the hidden xmin
            // column, so no physical column is added (LRN-004).
            e.Property<uint>("xmin").IsRowVersion();
        });

        modelBuilder.Entity<AccessGrant>(e =>
        {
            e.HasKey(g => g.Id);
            e.Property(g => g.PrincipalId).IsRequired().HasMaxLength(100);
            e.Property(g => g.TenantCode).IsRequired().HasMaxLength(50);
            e.Property(g => g.AccessPackageCode).IsRequired().HasMaxLength(100);
            e.Property(g => g.RevokedBy).HasMaxLength(100);
            e.HasIndex(g => g.PrincipalId);
            e.HasIndex(g => g.TenantCode);
            e.HasMany(g => g.Roles)
                .WithOne()
                .HasForeignKey(r => r.AccessGrantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AccessGrantRole>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.RoleName).IsRequired().HasMaxLength(50);
        });

        modelBuilder.Entity<AccessReviewCampaign>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Name).IsRequired().HasMaxLength(200);
            e.Property(c => c.TenantCode).IsRequired().HasMaxLength(50);
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(c => c.TenantCode);
            e.HasMany(c => c.Items)
                .WithOne()
                .HasForeignKey(i => i.CampaignId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AccessReviewItem>(e =>
        {
            e.HasKey(i => i.Id);
            e.Property(i => i.PrincipalId).IsRequired().HasMaxLength(100);
            e.Property(i => i.Decision).HasConversion<string>().HasMaxLength(30);
            e.Property(i => i.ReviewedBy).HasMaxLength(100);
            e.HasIndex(i => i.AccessGrantId);
        });
    }
}
