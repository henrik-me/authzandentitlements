namespace AuthzEntitlements.Authz.Pdp.Contracts;

// The fintech role names carried on Subject.Roles, mirroring Bank.Api's RoleNames. Shared
// domain vocabulary so the provider's eligibility rules and the scenario catalog agree.
public static class RoleNames
{
    public const string Teller = "Teller";
    public const string BranchManager = "BranchManager";
    public const string ComplianceOfficer = "ComplianceOfficer";
    public const string Auditor = "Auditor";
}
