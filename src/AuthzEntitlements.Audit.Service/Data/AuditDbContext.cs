using AuthzEntitlements.Audit.Service.Domain;
using Microsoft.EntityFrameworkCore;

namespace AuthzEntitlements.Audit.Service.Data;

// Owns the `audit` Postgres database: a single append-only, hash-chained table of authz
// decision audit entries. Mirrors the primary-constructor DbContext style used elsewhere in
// the solution. The Sequence PK is writer-assigned (ValueGeneratedNever) because the chain
// position is computed in-process by AuditChainWriter, not by a database identity column.
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options)
    : DbContext(options)
{
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.HasKey(a => a.Sequence);
            e.Property(a => a.Sequence).ValueGeneratedNever();

            e.Property(a => a.TimestampUtc).HasColumnType("timestamptz");
            e.Property(a => a.ReceivedAtUtc).HasColumnType("timestamptz");

            e.Property(a => a.TraceId).IsRequired().HasMaxLength(64);
            e.Property(a => a.Provider).IsRequired().HasMaxLength(100);
            e.Property(a => a.SubjectId).IsRequired().HasMaxLength(256);
            e.Property(a => a.Action).IsRequired().HasMaxLength(256);
            e.Property(a => a.ResourceType).IsRequired().HasMaxLength(128);
            e.Property(a => a.ResourceId).HasMaxLength(256);
            e.Property(a => a.Decision).IsRequired().HasMaxLength(32);
            e.Property(a => a.Reason).IsRequired().HasMaxLength(512);
            e.Property(a => a.Tenant).HasMaxLength(100);
            e.Property(a => a.Producer).IsRequired().HasMaxLength(50);

            // CS36 (LRN-057): the non-hashed replay snapshot. Nullable and bounded to the DEFAULT
            // size-guard cap (RequestSnapshotGuard.DefaultMaxSnapshotChars) so the persisted column
            // can never hold an over-size blob; the authoritative ingest cap is configurable and
            // defaults to this width. Not part of the row hash (like ReceivedAtUtc).
            e.Property(a => a.RequestSnapshot)
                .HasMaxLength(RequestSnapshotGuard.DefaultMaxSnapshotChars);

            // The chain hashes are fixed-width lowercase-hex SHA-256 digests.
            e.Property(a => a.PrevHash).IsRequired().IsFixedLength().HasMaxLength(64);
            e.Property(a => a.RowHash).IsRequired().IsFixedLength().HasMaxLength(64);

            // Row hashes are globally unique: a duplicate would indicate a replayed or forged
            // row, so the store rejects it at the database layer as a second line of defense.
            e.HasIndex(a => a.RowHash).IsUnique();

            e.HasIndex(a => a.SubjectId);
            e.HasIndex(a => a.Producer);
        });
    }
}
