namespace AuthzEntitlements.Bank.Api.Domain;

// The maker-checker gate for a single transaction (1:1). The maker is captured at
// creation; the checker is null until someone decides. Decide() enforces the
// segregation-of-duties invariant (checker != maker) and drives both its own state
// and the linked transaction's state. Role-based checker eligibility
// (BranchManager/ComplianceOfficer) is a precondition enforced by the caller.
public sealed class Approval
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public Guid MakerId { get; set; }
    public Guid? CheckerId { get; set; }
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;
    public string? DecisionReason { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }

    public Transaction Transaction { get; set; } = null!;

    // Records a checker's decision. Throws SegregationOfDutiesViolationException if
    // the checker is the maker, and InvalidOperationException if the approval was
    // already decided. Approving posts the transaction; rejecting rejects it.
    public void Decide(Guid checkerId, bool approve, string? reason, DateTimeOffset now)
    {
        if (Status != ApprovalStatus.Pending)
        {
            throw new InvalidOperationException(
                $"Approval {Id} is already decided ({Status}) and cannot be decided again.");
        }

        if (checkerId == MakerId)
        {
            throw new SegregationOfDutiesViolationException(MakerId, TransactionId);
        }

        CheckerId = checkerId;
        DecisionReason = reason;
        DecidedAt = now;

        if (approve)
        {
            Status = ApprovalStatus.Approved;
            Transaction.Status = TransactionStatus.Posted;
        }
        else
        {
            Status = ApprovalStatus.Rejected;
            Transaction.Status = TransactionStatus.Rejected;
        }
    }
}
