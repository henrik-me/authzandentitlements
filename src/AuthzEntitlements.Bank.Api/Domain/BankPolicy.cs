namespace AuthzEntitlements.Bank.Api.Domain;

// Central bank-policy constants that later authz/entitlements layers (PDP, quotas)
// will also consult. Kept here so the maker-checker threshold has a single source
// of truth shared by the domain factory, the seeder, and the endpoints.
public static class BankPolicy
{
    // Transactions at or above this amount require a second person (checker) to
    // approve before they post. Below it, they post immediately.
    public const decimal ApprovalThreshold = 10_000m;

    public static bool RequiresApproval(decimal amount) => amount >= ApprovalThreshold;
}

// The role names seeded in CS02. Later ABAC/RBAC layers resolve authorization from
// these; the maker-checker checker-eligibility rule references the two here.
public static class RoleNames
{
    public const string Teller = "Teller";
    public const string BranchManager = "BranchManager";
    public const string ComplianceOfficer = "ComplianceOfficer";
    public const string Auditor = "Auditor";

    public static readonly IReadOnlyList<string> All =
        [Teller, BranchManager, ComplianceOfficer, Auditor];

    // Only these roles may act as the checker on a maker-checker approval. The
    // domain enforces checker != maker (segregation of duties); role eligibility
    // is enforced at the endpoint/service layer before Approval.Decide is called.
    public static readonly IReadOnlySet<string> CheckerEligibleRoles =
        new HashSet<string>(StringComparer.Ordinal) { BranchManager, ComplianceOfficer };
}
