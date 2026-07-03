using AuthzEntitlements.Bank.Api.Domain;
using Xunit;

namespace AuthzEntitlements.Bank.Api.Tests;

// Pure-domain tests for the maker-checker + segregation-of-duties rules. No database
// is needed: the tests drive the domain factory and Approval.Decide directly.
public sealed class MakerCheckerTests
{
    private static readonly Guid Tenant = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Branch = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Account = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Maker = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Checker = new("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 1, 2, 9, 0, 0, TimeSpan.Zero);

    private static (Transaction Txn, Approval Approval) CreateHighValue()
    {
        var (txn, approval) = Transaction.Create(
            Tenant, Branch, Account, TransactionType.Transfer,
            BankPolicy.ApprovalThreshold, "USD", Maker, "test", Now);
        Assert.NotNull(approval);
        return (txn, approval!);
    }

    [Fact]
    public void Decide_ByMaker_ThrowsSegregationOfDutiesViolation()
    {
        var (txn, approval) = CreateHighValue();

        var ex = Assert.Throws<SegregationOfDutiesViolationException>(
            () => approval.Decide(Maker, approve: true, "self-approval", Now));

        Assert.Equal(Maker, ex.MakerId);
        Assert.Equal(txn.Id, ex.TransactionId);
        // State must be untouched by a rejected self-approval.
        Assert.Equal(ApprovalStatus.Pending, approval.Status);
        Assert.Equal(TransactionStatus.Pending, txn.Status);
        Assert.Null(approval.CheckerId);
    }

    [Fact]
    public void Decide_ByDifferentChecker_Approves_PostsTransaction()
    {
        var (txn, approval) = CreateHighValue();

        approval.Decide(Checker, approve: true, "looks good", Now);

        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.Equal(TransactionStatus.Posted, txn.Status);
        Assert.Equal(Checker, approval.CheckerId);
        Assert.Equal(Now, approval.DecidedAt);
    }

    [Fact]
    public void Decide_ByDifferentChecker_Rejects_RejectsTransaction()
    {
        var (txn, approval) = CreateHighValue();

        approval.Decide(Checker, approve: false, "insufficient documentation", Now);

        Assert.Equal(ApprovalStatus.Rejected, approval.Status);
        Assert.Equal(TransactionStatus.Rejected, txn.Status);
        Assert.Equal(Checker, approval.CheckerId);
    }

    [Fact]
    public void Decide_Twice_ThrowsInvalidOperation()
    {
        var (_, approval) = CreateHighValue();
        approval.Decide(Checker, approve: true, "first", Now);

        Assert.Throws<InvalidOperationException>(
            () => approval.Decide(Checker, approve: false, "second", Now));
    }

    [Fact]
    public void Create_BelowThreshold_PostsImmediately_NoApproval()
    {
        var (txn, approval) = Transaction.Create(
            Tenant, Branch, Account, TransactionType.Debit,
            BankPolicy.ApprovalThreshold - 0.01m, "USD", Maker, "small", Now);

        Assert.Null(approval);
        Assert.Equal(TransactionStatus.Posted, txn.Status);
    }

    [Fact]
    public void Create_AtOrAboveThreshold_IsPending_WithPendingApproval()
    {
        var (txn, approval) = Transaction.Create(
            Tenant, Branch, Account, TransactionType.Debit,
            BankPolicy.ApprovalThreshold, "USD", Maker, "large", Now);

        Assert.NotNull(approval);
        Assert.Equal(TransactionStatus.Pending, txn.Status);
        Assert.Equal(ApprovalStatus.Pending, approval!.Status);
        Assert.Equal(Maker, approval.MakerId);
        Assert.Null(approval.CheckerId);
        Assert.Equal(txn.Id, approval.TransactionId);
    }
}
