using AuthzEntitlements.Bank.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AuthzEntitlements.Bank.Api.Data;

public sealed class BankDbContext(DbContextOptions<BankDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Approval> Approvals => Set<Approval>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Tenant>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Name).IsRequired().HasMaxLength(200);
            e.Property(t => t.Code).IsRequired().HasMaxLength(50);
            e.HasIndex(t => t.Code).IsUnique();
        });

        modelBuilder.Entity<Region>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Name).IsRequired().HasMaxLength(200);
            e.Property(r => r.Code).IsRequired().HasMaxLength(50);
            e.HasIndex(r => new { r.TenantId, r.Code }).IsUnique();
            e.HasOne(r => r.Tenant)
                .WithMany(t => t.Regions)
                .HasForeignKey(r => r.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Branch>(e =>
        {
            e.HasKey(b => b.Id);
            e.Property(b => b.Name).IsRequired().HasMaxLength(200);
            e.Property(b => b.Code).IsRequired().HasMaxLength(50);
            e.HasIndex(b => new { b.TenantId, b.Code }).IsUnique();
            e.HasOne(b => b.Tenant)
                .WithMany(t => t.Branches)
                .HasForeignKey(b => b.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(b => b.Region)
                .WithMany(r => r.Branches)
                .HasForeignKey(b => b.RegionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Name).IsRequired().HasMaxLength(100);
            e.Property(r => r.Description).IsRequired().HasMaxLength(500);
            e.HasIndex(r => r.Name).IsUnique();
        });

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Username).IsRequired().HasMaxLength(100);
            e.Property(u => u.Email).IsRequired().HasMaxLength(256);
            e.Property(u => u.DisplayName).IsRequired().HasMaxLength(200);
            e.HasIndex(u => new { u.TenantId, u.Username }).IsUnique();
            e.HasOne(u => u.Tenant)
                .WithMany(t => t.Users)
                .HasForeignKey(u => u.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(u => u.Branch)
                .WithMany()
                .HasForeignKey(u => u.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<UserRole>(e =>
        {
            e.HasKey(ur => new { ur.UserId, ur.RoleId });
            e.HasOne(ur => ur.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(ur => ur.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ur => ur.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(ur => ur.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Account>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.AccountNumber).IsRequired().HasMaxLength(34);
            e.Property(a => a.CustomerName).IsRequired().HasMaxLength(200);
            e.Property(a => a.Currency).IsRequired().HasMaxLength(3);
            e.Property(a => a.Balance).HasPrecision(18, 2);
            e.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(a => a.AccountNumber).IsUnique();
            e.HasOne(a => a.Tenant)
                .WithMany()
                .HasForeignKey(a => a.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Branch)
                .WithMany()
                .HasForeignKey(a => a.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Transaction>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Currency).IsRequired().HasMaxLength(3);
            e.Property(t => t.Amount).HasPrecision(18, 2);
            e.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(t => t.Reference).HasMaxLength(200);
            e.HasOne(t => t.Account)
                .WithMany(a => a.Transactions)
                .HasForeignKey(t => t.AccountId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.Maker)
                .WithMany()
                .HasForeignKey(t => t.MakerId)
                .OnDelete(DeleteBehavior.Restrict);
            // Tenant/branch are trustworthy ABAC attributes derived from the account;
            // enforce them with FKs. Restrict avoids multiple-cascade-path conflicts
            // (Tenant/Branch already cascade to Account, which cascades to Transaction).
            e.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(t => t.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>()
                .WithMany()
                .HasForeignKey(t => t.BranchId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Approval>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(a => a.DecisionReason).HasMaxLength(500);
            e.HasIndex(a => a.TransactionId).IsUnique();
            e.HasOne(a => a.Transaction)
                .WithOne(t => t.Approval)
                .HasForeignKey<Approval>(a => a.TransactionId)
                .OnDelete(DeleteBehavior.Cascade);
            // Optimistic concurrency: two concurrent approve/reject calls both read
            // Pending in memory and pass the domain guard; the xmin system-column
            // token makes the second SaveChanges fail (DbUpdateConcurrencyException)
            // instead of last-writer-wins, preserving the "decide once" invariant.
            // (Npgsql maps a uint row-version property to the hidden xmin system
            // column, so no real column is added.)
            e.Property<uint>("xmin").IsRowVersion();
        });
    }
}
