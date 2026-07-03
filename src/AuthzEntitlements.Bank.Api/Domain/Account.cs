namespace AuthzEntitlements.Bank.Api.Domain;

// A customer account held at a branch. Balance is money (18,2). The account is the
// resource most authz scenarios target (read/credit/debit/freeze).
public sealed class Account
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public AccountType Type { get; set; }
    public decimal Balance { get; set; }
    public string Currency { get; set; } = "USD";
    public AccountStatus Status { get; set; } = AccountStatus.Active;

    public Tenant Tenant { get; set; } = null!;
    public Branch Branch { get; set; } = null!;
    public ICollection<Transaction> Transactions { get; } = [];
}
