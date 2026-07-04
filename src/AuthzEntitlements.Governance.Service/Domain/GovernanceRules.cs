namespace AuthzEntitlements.Governance.Service.Domain;

// Pure governance rules shared by the endpoints and directly unit-testable. Keeping the
// maker-checker, JIT-duration, and expiry arithmetic out of the I/O handlers means the
// security-critical decisions are exercised without a web host or database.
public static class GovernanceRules
{
    // Stable reason codes + human messages for the two checker gates, shared by the approve
    // path (AccessApprovalService) and the reject path (the endpoint) so the audit CODE
    // (which CS13 matches on) and the message never drift between them.
    public const string MakerEqualsCheckerCode = "MakerEqualsChecker";
    public const string MakerEqualsCheckerMessage = "the checker (approver) must differ from the requester";
    public const string ApproverNotEligibleCode = "ApproverNotEligible";
    public const string ApproverNotEligibleMessage =
        "the approver must be a known principal with a checker-eligible role "
        + "(BranchManager or ComplianceOfficer)";

    // Maker-checker (segregation of duties on the approval action itself): the approver
    // or rejector MUST differ from the requester, so a principal cannot decide their own
    // elevation. Compared ordinally on the trusted request PrincipalId.
    public static bool CheckerDiffersFromRequester(string requesterId, string checkerId) =>
        !string.Equals(requesterId, checkerId, StringComparison.Ordinal);

    // Only these oversight roles may act as the checker (approver/rejector) on a JIT
    // elevation. Mirrors Bank.Api's RoleNames.CheckerEligibleRoles: a Teller (a maker) or
    // an Auditor (who must stay independent) may not sign off. Combined with the known-
    // principal lookup at the endpoint, this makes maker-checker meaningful — a random or
    // unknown/spoofed approver id cannot approve a peer's elevation.
    public static readonly IReadOnlySet<string> CheckerEligibleRoles =
        new HashSet<string>(StringComparer.Ordinal)
        {
            GovernanceCatalog.Roles.BranchManager,
            GovernanceCatalog.Roles.ComplianceOfficer,
        };

    // True when the role set holds at least one checker-eligible role. An empty set (e.g.
    // an unknown principal with no baseline roles) is never eligible.
    public static bool IsCheckerEligible(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return roles.Any(CheckerEligibleRoles.Contains);
    }

    // The effective JIT lifetime: the requested duration when it is a positive override,
    // otherwise the package default. A non-positive or absent request falls back to the
    // package default rather than issuing a zero-length (already-expired) grant.
    public static int EffectiveDurationMinutes(int? requestedMinutes, int packageDefaultMinutes) =>
        requestedMinutes is > 0 ? requestedMinutes.Value : packageDefaultMinutes;

    // Expiry instant for a grant issued at grantedAt for the given effective duration.
    public static DateTimeOffset ComputeExpiry(DateTimeOffset grantedAt, int durationMinutes) =>
        grantedAt.AddMinutes(durationMinutes);
}
