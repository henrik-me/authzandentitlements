namespace AuthzEntitlements.Bank.Api.Domain;

// A money movement against an account, created by a "maker". The static factory
// applies the maker-checker threshold rule: at/above BankPolicy.ApprovalThreshold
// the transaction is created Pending with a paired Pending Approval; below it the
// transaction is Posted immediately with no approval. Keeping this on the entity
// gives the seeder and the endpoints one shared, testable code path.
public sealed class Transaction
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public TransactionStatus Status { get; set; }
    public Guid MakerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? Reference { get; set; }

    public Account Account { get; set; } = null!;
    public User Maker { get; set; } = null!;
    public Approval? Approval { get; set; }

    // Creates a transaction and, when the amount trips the approval threshold, the
    // paired Pending approval. The two objects are wired together in memory so the
    // result is usable both by EF (it tracks the graph) and by unit tests (no DB).
    public static (Transaction Transaction, Approval? Approval) Create(
        Guid tenantId,
        Guid branchId,
        Guid accountId,
        TransactionType type,
        decimal amount,
        string currency,
        Guid makerId,
        string? reference,
        DateTimeOffset now)
    {
        if (amount <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount), amount, "Transaction amount must be positive.");
        }

        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            AccountId = accountId,
            Type = type,
            Amount = amount,
            Currency = currency,
            MakerId = makerId,
            Reference = reference,
            CreatedAt = now,
        };

        if (!BankPolicy.RequiresApproval(amount))
        {
            transaction.Status = TransactionStatus.Posted;
            return (transaction, null);
        }

        transaction.Status = TransactionStatus.Pending;
        var approval = new Approval
        {
            Id = Guid.NewGuid(),
            TransactionId = transaction.Id,
            Transaction = transaction,
            MakerId = makerId,
            Status = ApprovalStatus.Pending,
            RequestedAt = now,
        };
        transaction.Approval = approval;
        return (transaction, approval);
    }
}
