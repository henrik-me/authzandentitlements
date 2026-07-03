using AuthzEntitlements.Entitlements.Service.Domain;
using Microsoft.EntityFrameworkCore;

namespace AuthzEntitlements.Entitlements.Service.Data;

// Owns the `entitlements` Postgres database (plans, modules, quotas, subscriptions,
// seats, usage). Mirrors the Bank.Api primary-constructor DbContext style: enums are
// stored as strings and every lookup key has a unique index so the entitlement
// queries stay cheap and unambiguous.
public sealed class EntitlementsDbContext(DbContextOptions<EntitlementsDbContext> options)
    : DbContext(options)
{
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PlanModule> PlanModules => Set<PlanModule>();
    public DbSet<PlanQuota> PlanQuotas => Set<PlanQuota>();
    public DbSet<TenantSubscription> Subscriptions => Set<TenantSubscription>();
    public DbSet<SeatAssignment> SeatAssignments => Set<SeatAssignment>();
    public DbSet<UsageCounter> UsageCounters => Set<UsageCounter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Plan>(e =>
        {
            e.HasKey(p => p.Tier);
            e.Property(p => p.Tier).HasConversion<string>().HasMaxLength(30);
            e.HasMany(p => p.Modules)
                .WithOne(m => m.Plan)
                .HasForeignKey(m => m.PlanTier)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Quotas)
                .WithOne(q => q.Plan)
                .HasForeignKey(q => q.PlanTier)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PlanModule>(e =>
        {
            e.HasKey(m => new { m.PlanTier, m.ModuleKey });
            e.Property(m => m.PlanTier).HasConversion<string>().HasMaxLength(30);
            e.Property(m => m.ModuleKey).IsRequired().HasMaxLength(50);
        });

        modelBuilder.Entity<PlanQuota>(e =>
        {
            e.HasKey(q => new { q.PlanTier, q.QuotaKey });
            e.Property(q => q.PlanTier).HasConversion<string>().HasMaxLength(30);
            e.Property(q => q.QuotaKey).IsRequired().HasMaxLength(50);
        });

        modelBuilder.Entity<TenantSubscription>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.TenantCode).IsRequired().HasMaxLength(50);
            e.Property(s => s.PlanTier).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(s => s.TenantCode).IsUnique();
            e.HasMany(s => s.Seats)
                .WithOne(a => a.Subscription)
                .HasForeignKey(a => a.SubscriptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SeatAssignment>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => new { a.SubscriptionId, a.UserId }).IsUnique();
        });

        modelBuilder.Entity<UsageCounter>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.TenantCode).IsRequired().HasMaxLength(50);
            e.Property(u => u.QuotaKey).IsRequired().HasMaxLength(50);
            e.Property(u => u.PeriodKey).IsRequired().HasMaxLength(10);
            e.HasIndex(u => new { u.TenantCode, u.QuotaKey, u.PeriodKey }).IsUnique();
            // Optimistic concurrency for the consume race: two concurrent consume calls
            // both read the same Used and would otherwise last-writer-wins. The xmin
            // system-column token makes the losing SaveChanges throw so the endpoint can
            // reload and retry rather than silently over-granting quota. Npgsql maps the
            // shadow uint to the hidden xmin column, so no physical column is added
            // (LRN-004: UseXminAsConcurrencyToken was removed in Npgsql 10 rc1).
            e.Property<uint>("xmin").IsRowVersion();
        });
    }
}
