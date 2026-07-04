namespace AuthzEntitlements.Authz.Pdp.Contracts;

// A machine-stable Code plus a human Message explaining a decision. The Code is the
// contract other layers (audit, playground, tests, adapter parity checks) match on, so
// it must stay stable even if the Message wording changes.
public sealed record Reason(string Code, string Message);

// Stable reason codes shared by the reference provider and every adapter so a decision explains
// itself the same way across engines. Most map 1:1 to the Bank.Api enforcement rules; SodConflict
// is the CS11 governance segregation-of-duties verdict (it maps to no Bank.Api rule). UnknownAction
// is the fail-closed code for an action outside ActionNames.
public static class ReasonCodes
{
    public const string Permit = "Permit";
    public const string MissingScope = "MissingScope";
    public const string TenantMismatch = "TenantMismatch";
    public const string RoleNotAuthorized = "RoleNotAuthorized";
    public const string SubjectNotMaker = "SubjectNotMaker";
    public const string MakerEqualsChecker = "MakerEqualsChecker";
    public const string NotPending = "NotPending";
    public const string BranchNotInTenant = "BranchNotInTenant";

    // CS11 governance segregation-of-duties verdict: the reason a governance.access.request denies a
    // toxic role combination. Shared so the reference and OPA engines explain a SoD denial
    // identically — and so it survives the OPA adapter's bounded-reason allow-list.
    public const string SodConflict = "SodConflict";

    public const string UnknownAction = "UnknownAction";
}
