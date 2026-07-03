using AuthzEntitlements.Bank.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AuthzEntitlements.Bank.Api.Data;

// Deterministic, idempotent seed. All identifiers are fixed so re-running against a
// fresh database yields the same graph and later CSs (PDP scenarios, audit) can
// reference known IDs. Guarded on Tenants.AnyAsync so it never double-seeds.
public static class BankSeeder
{
    // Tenants
    public static readonly Guid ContosoTenantId = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid FabrikamTenantId = new("22222222-2222-2222-2222-222222222222");

    // Regions
    public static readonly Guid NorthRegionId = new("10000000-0000-0000-0000-000000000001");
    public static readonly Guid SouthRegionId = new("10000000-0000-0000-0000-000000000002");
    public static readonly Guid FabrikamRegionId = new("10000000-0000-0000-0000-000000000003");

    // Branches
    public static readonly Guid NorthMainBranchId = new("20000000-0000-0000-0000-000000000001");
    public static readonly Guid SouthDowntownBranchId = new("20000000-0000-0000-0000-000000000002");
    public static readonly Guid FabrikamHqBranchId = new("20000000-0000-0000-0000-000000000003");

    // Roles
    public static readonly Guid TellerRoleId = new("30000000-0000-0000-0000-000000000001");
    public static readonly Guid BranchManagerRoleId = new("30000000-0000-0000-0000-000000000002");
    public static readonly Guid ComplianceRoleId = new("30000000-0000-0000-0000-000000000003");
    public static readonly Guid AuditorRoleId = new("30000000-0000-0000-0000-000000000004");

    // Users
    public static readonly Guid Teller1Id = new("40000000-0000-0000-0000-000000000001");
    public static readonly Guid Manager1Id = new("40000000-0000-0000-0000-000000000002");
    public static readonly Guid Compliance1Id = new("40000000-0000-0000-0000-000000000003");
    public static readonly Guid Auditor1Id = new("40000000-0000-0000-0000-000000000004");
    public static readonly Guid FabrikamTeller1Id = new("40000000-0000-0000-0000-000000000005");

    // Accounts
    public static readonly Guid CheckingAccountId = new("50000000-0000-0000-0000-000000000001");
    public static readonly Guid SavingsAccountId = new("50000000-0000-0000-0000-000000000002");
    public static readonly Guid LoanAccountId = new("50000000-0000-0000-0000-000000000003");
    public static readonly Guid FabrikamAccountId = new("50000000-0000-0000-0000-000000000004");

    // Transactions (fixed so audit/PDP scenarios can reference them)
    public static readonly Guid PostedTxnId = new("60000000-0000-0000-0000-000000000001");
    public static readonly Guid PendingTxnId = new("60000000-0000-0000-0000-000000000002");
    public static readonly Guid ApprovedTxnId = new("60000000-0000-0000-0000-000000000003");
    public static readonly Guid PendingApprovalId = new("70000000-0000-0000-0000-000000000002");
    public static readonly Guid ApprovedApprovalId = new("70000000-0000-0000-0000-000000000003");

    private static readonly DateTimeOffset SeedTime =
        new(2026, 1, 2, 9, 0, 0, TimeSpan.Zero);

    public static async Task SeedAsync(BankDbContext db, CancellationToken ct = default)
    {
        if (await db.Tenants.AnyAsync(ct))
        {
            return;
        }

        db.Tenants.AddRange(
            new Tenant { Id = ContosoTenantId, Name = "Contoso Bank", Code = "CONTOSO" },
            new Tenant { Id = FabrikamTenantId, Name = "Fabrikam Bank", Code = "FABRIKAM" });

        db.Regions.AddRange(
            new Region { Id = NorthRegionId, TenantId = ContosoTenantId, Name = "North", Code = "N" },
            new Region { Id = SouthRegionId, TenantId = ContosoTenantId, Name = "South", Code = "S" },
            new Region { Id = FabrikamRegionId, TenantId = FabrikamTenantId, Name = "Central", Code = "C" });

        db.Branches.AddRange(
            new Branch
            {
                Id = NorthMainBranchId, TenantId = ContosoTenantId, RegionId = NorthRegionId,
                Name = "North Main", Code = "NM01",
            },
            new Branch
            {
                Id = SouthDowntownBranchId, TenantId = ContosoTenantId, RegionId = SouthRegionId,
                Name = "South Downtown", Code = "SD01",
            },
            new Branch
            {
                Id = FabrikamHqBranchId, TenantId = FabrikamTenantId, RegionId = FabrikamRegionId,
                Name = "Fabrikam HQ", Code = "FH01",
            });

        db.Roles.AddRange(
            new Role { Id = TellerRoleId, Name = RoleNames.Teller, Description = "Front-line teller." },
            new Role
            {
                Id = BranchManagerRoleId, Name = RoleNames.BranchManager,
                Description = "Branch manager; may approve high-value transactions.",
            },
            new Role
            {
                Id = ComplianceRoleId, Name = RoleNames.ComplianceOfficer,
                Description = "Compliance officer; may approve high-value transactions.",
            },
            new Role
            {
                Id = AuditorRoleId, Name = RoleNames.Auditor,
                Description = "Read-only auditor.",
            });

        db.Users.AddRange(
            new User
            {
                Id = Teller1Id, TenantId = ContosoTenantId, BranchId = NorthMainBranchId,
                Username = "teller1", Email = "teller1@contoso.example", DisplayName = "Tara Teller",
            },
            new User
            {
                Id = Manager1Id, TenantId = ContosoTenantId, BranchId = NorthMainBranchId,
                Username = "manager1", Email = "manager1@contoso.example", DisplayName = "Mona Manager",
            },
            new User
            {
                Id = Compliance1Id, TenantId = ContosoTenantId, BranchId = NorthMainBranchId,
                Username = "compliance1", Email = "compliance1@contoso.example",
                DisplayName = "Colin Compliance",
            },
            new User
            {
                Id = Auditor1Id, TenantId = ContosoTenantId, BranchId = NorthMainBranchId,
                Username = "auditor1", Email = "auditor1@contoso.example", DisplayName = "Alan Auditor",
            },
            new User
            {
                Id = FabrikamTeller1Id, TenantId = FabrikamTenantId, BranchId = FabrikamHqBranchId,
                Username = "teller1", Email = "teller1@fabrikam.example", DisplayName = "Felix Fabrikam",
            });

        db.UserRoles.AddRange(
            new UserRole { UserId = Teller1Id, RoleId = TellerRoleId },
            new UserRole { UserId = Manager1Id, RoleId = BranchManagerRoleId },
            new UserRole { UserId = Compliance1Id, RoleId = ComplianceRoleId },
            new UserRole { UserId = Auditor1Id, RoleId = AuditorRoleId },
            new UserRole { UserId = FabrikamTeller1Id, RoleId = TellerRoleId });

        db.Accounts.AddRange(
            new Account
            {
                Id = CheckingAccountId, TenantId = ContosoTenantId, BranchId = NorthMainBranchId,
                AccountNumber = "CONTOSO-CHK-0001", CustomerName = "Alice Anderson",
                Type = AccountType.Checking, Balance = 4_200.00m, Currency = "USD",
                Status = AccountStatus.Active,
            },
            new Account
            {
                Id = SavingsAccountId, TenantId = ContosoTenantId, BranchId = NorthMainBranchId,
                AccountNumber = "CONTOSO-SAV-0001", CustomerName = "Bob Brown",
                Type = AccountType.Savings, Balance = 58_000.00m, Currency = "USD",
                Status = AccountStatus.Active,
            },
            new Account
            {
                Id = LoanAccountId, TenantId = ContosoTenantId, BranchId = SouthDowntownBranchId,
                AccountNumber = "CONTOSO-LON-0001", CustomerName = "Carol Carter",
                Type = AccountType.Loan, Balance = 125_000.00m, Currency = "USD",
                Status = AccountStatus.Active,
            },
            new Account
            {
                Id = FabrikamAccountId, TenantId = FabrikamTenantId, BranchId = FabrikamHqBranchId,
                AccountNumber = "FABRIKAM-CHK-0001", CustomerName = "Dan Davis",
                Type = AccountType.Checking, Balance = 9_900.00m, Currency = "USD",
                Status = AccountStatus.Active,
            });

        SeedTransactions(db);

        await db.SaveChangesAsync(ct);
    }

    // Exercises all three maker-checker paths with the shared Transaction.Create
    // factory so the seed data matches the runtime code path exactly.
    private static void SeedTransactions(BankDbContext db)
    {
        // (a) Below threshold -> Posted immediately, no approval.
        var (posted, postedApproval) = Transaction.Create(
            ContosoTenantId, NorthMainBranchId, CheckingAccountId, TransactionType.Debit,
            250.00m, "USD", Teller1Id, "ATM withdrawal", SeedTime);
        posted.Id = PostedTxnId;
        db.Transactions.Add(posted);
        // postedApproval is null by construction below threshold.
        _ = postedApproval;

        // (b) At/above threshold -> Pending with a Pending approval (checker null).
        var (pending, pendingApproval) = Transaction.Create(
            ContosoTenantId, NorthMainBranchId, SavingsAccountId, TransactionType.Transfer,
            15_000.00m, "USD", Teller1Id, "Wire transfer", SeedTime);
        pending.Id = PendingTxnId;
        pendingApproval!.Id = PendingApprovalId;
        pendingApproval.TransactionId = pending.Id;
        db.Transactions.Add(pending);

        // (c) At/above threshold, approved by manager1 (checker != maker -> SoD valid).
        var (approved, approvedApproval) = Transaction.Create(
            ContosoTenantId, NorthMainBranchId, SavingsAccountId, TransactionType.Debit,
            25_000.00m, "USD", Teller1Id, "Loan disbursement", SeedTime);
        approved.Id = ApprovedTxnId;
        approvedApproval!.Id = ApprovedApprovalId;
        approvedApproval.TransactionId = approved.Id;
        approvedApproval.Decide(Manager1Id, approve: true, "Reviewed and approved", SeedTime);
        db.Transactions.Add(approved);
    }
}
