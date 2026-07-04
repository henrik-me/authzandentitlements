using AuthzEntitlements.Bank.Web.Clients;

namespace AuthzEntitlements.Bank.Web.ViewModels;

// Pure, dependency-free helpers for the CHECKER maker-checker approvals page. Kept out of
// the .razor so the filtering, eligibility hint, and outcome-labelling are unit-testable
// offline (no server, Docker, or Keycloak). NONE of this is the security boundary: the
// UI eligibility hint is a courtesy only — Bank.Api independently enforces checker-role
// eligibility, segregation of duties, and decide-once (defense in depth, fail-closed).
public static class ApprovalsModel
{
    // Roles a checker must hold to be allowed to decide an approval. Mirrors
    // Bank.Api RoleNames.CheckerEligibleRoles (BankPolicy.cs) — but this copy only drives
    // a UI hint; the server is the authority.
    public static readonly IReadOnlySet<string> CheckerEligibleRoles =
        new HashSet<string>(StringComparer.Ordinal) { "BranchManager", "ComplianceOfficer" };

    // Decidable items: a transaction still Pending that carries a Pending approval (created
    // when a maker submits an amount at/above the approval threshold), oldest request first.
    public static IReadOnlyList<TransactionDto> PendingApprovals(
        IEnumerable<TransactionDto> transactions) =>
        transactions
            .Where(t => t.Status == TransactionStatus.Pending
                && t.Approval is not null
                && t.Approval.Status == ApprovalStatus.Pending)
            .OrderBy(t => t.Approval!.RequestedAt)
            .ToList();

    // True when the signed-in roles intersect the checker-eligible set. Drives a UI hint
    // ONLY — the page still sends the request so the server's fail-closed response is
    // visible (defense in depth).
    public static bool IsCheckerEligible(IEnumerable<string> roles) =>
        roles.Any(CheckerEligibleRoles.Contains);

    // Human-readable label for a decide outcome status code, surfacing the fine-authz /
    // SoD / decide-once semantics the server enforces.
    public static string DecisionOutcomeLabel(int statusCode) => statusCode switch
    {
        200 => "Decided",
        403 => "403 Forbidden (checker role not eligible, or coarse gateway)",
        409 => "409 Conflict (segregation of duties, or already decided)",
        400 => "400 Bad Request (unknown checker)",
        404 => "404 Not Found (not in your tenant)",
        503 => "503 Service Unavailable (fail-closed)",
        _ => $"{statusCode}",
    };
}

// Form model for the Approve action. TransactionId is chosen from the pending list; the
// CheckerId is NEVER a form field — it is bound at submit time from the resolved token
// identity (fail-closed authz — a caller may not decide as another subject).
public sealed class ApproveInput
{
    public Guid TransactionId { get; set; }

    public string? Reason { get; set; }
}

// Form model for the Reject action. Same shape as ApproveInput; a distinct type keeps the
// two static-SSR forms disambiguated by FormName.
public sealed class RejectInput
{
    public Guid TransactionId { get; set; }

    public string? Reason { get; set; }
}
