using AuthzEntitlements.Bank.Web.Clients;
using AuthzEntitlements.Bank.Web.ViewModels;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

// Offline unit tests for the CHECKER approvals page model. No server, Docker, or Keycloak
// required — they exercise the pure pending-filter, the checker-eligibility UI hint (which
// is NOT the security boundary), and the decide-outcome labelling that surfaces the
// SoD / decide-once / fine-authz semantics the server enforces.
public class ApprovalsModelTests
{
    private static ApprovalDto Approval(ApprovalStatus status, DateTimeOffset requestedAt) =>
        new(
            Id: Guid.NewGuid(),
            TransactionId: Guid.NewGuid(),
            MakerId: Guid.NewGuid(),
            CheckerId: null,
            Status: status,
            DecisionReason: null,
            RequestedAt: requestedAt,
            DecidedAt: null);

    private static TransactionDto Transaction(
        TransactionStatus status, ApprovalDto? approval, Guid? id = null) =>
        new(
            Id: id ?? Guid.NewGuid(),
            AccountId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            BranchId: Guid.NewGuid(),
            Type: TransactionType.Debit,
            Amount: 5000m,
            Currency: "USD",
            Status: status,
            MakerId: Guid.NewGuid(),
            CreatedAt: DateTimeOffset.UnixEpoch,
            Reference: null,
            Approval: approval);

    [Fact]
    public void PendingApprovals_keeps_only_pending_transactions_with_a_pending_approval()
    {
        var included = Transaction(TransactionStatus.Pending, Approval(ApprovalStatus.Pending, DateTimeOffset.UnixEpoch));
        var postedApproved = Transaction(TransactionStatus.Posted, Approval(ApprovalStatus.Approved, DateTimeOffset.UnixEpoch));
        var rejected = Transaction(TransactionStatus.Rejected, Approval(ApprovalStatus.Rejected, DateTimeOffset.UnixEpoch));
        var approvalNull = Transaction(TransactionStatus.Pending, approval: null);
        var pendingTxnDecidedApproval = Transaction(TransactionStatus.Pending, Approval(ApprovalStatus.Approved, DateTimeOffset.UnixEpoch));

        var result = ApprovalsModel.PendingApprovals(
            [included, postedApproved, rejected, approvalNull, pendingTxnDecidedApproval]);

        Assert.Single(result);
        Assert.Equal(included.Id, result[0].Id);
    }

    [Fact]
    public void PendingApprovals_orders_by_approval_requested_at_ascending()
    {
        var newest = Guid.NewGuid();
        var oldest = Guid.NewGuid();
        var middle = Guid.NewGuid();

        var txns = new[]
        {
            Transaction(TransactionStatus.Pending, Approval(ApprovalStatus.Pending, new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)), newest),
            Transaction(TransactionStatus.Pending, Approval(ApprovalStatus.Pending, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)), oldest),
            Transaction(TransactionStatus.Pending, Approval(ApprovalStatus.Pending, new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)), middle),
        };

        var result = ApprovalsModel.PendingApprovals(txns);

        Assert.Equal([oldest, middle, newest], result.Select(t => t.Id));
    }

    [Fact]
    public void PendingApprovals_is_empty_for_no_input()
    {
        Assert.Empty(ApprovalsModel.PendingApprovals([]));
    }

    [Theory]
    [InlineData("BranchManager")]
    [InlineData("ComplianceOfficer")]
    public void IsCheckerEligible_is_true_for_a_checker_eligible_role(string role)
    {
        Assert.True(ApprovalsModel.IsCheckerEligible([role]));
    }

    [Fact]
    public void IsCheckerEligible_is_true_when_any_role_is_eligible()
    {
        Assert.True(ApprovalsModel.IsCheckerEligible(["Teller", "ComplianceOfficer"]));
    }

    [Theory]
    [InlineData("Teller")]
    [InlineData("Auditor")]
    public void IsCheckerEligible_is_false_for_a_non_eligible_role(string role)
    {
        Assert.False(ApprovalsModel.IsCheckerEligible([role]));
    }

    [Fact]
    public void IsCheckerEligible_is_false_for_no_roles()
    {
        Assert.False(ApprovalsModel.IsCheckerEligible([]));
    }

    [Fact]
    public void CheckerEligibleRoles_is_exactly_branch_manager_and_compliance_officer()
    {
        Assert.Equal(
            new HashSet<string> { "BranchManager", "ComplianceOfficer" },
            ApprovalsModel.CheckerEligibleRoles.ToHashSet());
    }

    [Theory]
    [InlineData(200, "Decided")]
    [InlineData(403, "403 Forbidden (checker role not eligible, or coarse gateway)")]
    [InlineData(409, "409 Conflict (segregation of duties, or already decided)")]
    [InlineData(400, "400 Bad Request (unknown checker)")]
    [InlineData(404, "404 Not Found (not in your tenant)")]
    [InlineData(503, "503 Service Unavailable (fail-closed)")]
    public void DecisionOutcomeLabel_maps_known_status_codes(int statusCode, string expected)
    {
        Assert.Equal(expected, ApprovalsModel.DecisionOutcomeLabel(statusCode));
    }

    [Fact]
    public void DecisionOutcomeLabel_falls_back_to_the_raw_code_for_unknown_status()
    {
        Assert.Equal("418", ApprovalsModel.DecisionOutcomeLabel(418));
    }
}
