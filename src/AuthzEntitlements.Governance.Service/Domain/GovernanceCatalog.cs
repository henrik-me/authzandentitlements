namespace AuthzEntitlements.Governance.Service.Domain;

// Well-known governance vocabulary as string constants so the seeder, endpoints, and
// tests all agree on the same values. The fintech role names mirror the PDP's RoleNames
// (Teller/BranchManager/ComplianceOfficer/Auditor) — the governance service carries its
// own copy of the strings rather than referencing the PDP project, which it only calls
// over HTTP.
public static class GovernanceCatalog
{
    // Fintech roles a principal can hold or an access package can grant. Kept in sync
    // (by value) with the PDP's RoleNames so the SoD wire payload uses the exact strings
    // the reference/OPA engines evaluate.
    public static class Roles
    {
        public const string Teller = "Teller";
        public const string BranchManager = "BranchManager";
        public const string ComplianceOfficer = "ComplianceOfficer";
        public const string Auditor = "Auditor";
    }

    // Seeded access-package codes referenced by scenarios and tests.
    public static class Packages
    {
        public const string QuarterEndClose = "quarter-end-close";
        public const string TreasuryOversight = "treasury-oversight";
        public const string BranchApprover = "branch-approver";
    }
}
