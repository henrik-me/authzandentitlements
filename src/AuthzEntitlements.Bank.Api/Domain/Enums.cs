namespace AuthzEntitlements.Bank.Api.Domain;

// Persisted as strings via HasConversion<string>() so the database stores stable,
// human-readable values rather than brittle ordinal integers.

public enum AccountType
{
    Checking,
    Savings,
    Loan,
}

public enum AccountStatus
{
    Active,
    Frozen,
    Closed,
}

public enum TransactionType
{
    Debit,
    Credit,
    Transfer,
}

public enum TransactionStatus
{
    Pending,
    Approved,
    Rejected,
    Posted,
}

public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
}
