namespace AuthzEntitlements.Governance.Service.Domain;

// Pure governance rules shared by the endpoints and directly unit-testable. Keeping the
// maker-checker, JIT-duration, and expiry arithmetic out of the I/O handlers means the
// security-critical decisions are exercised without a web host or database.
public static class GovernanceRules
{
    // Maker-checker (segregation of duties on the approval action itself): the approver
    // or rejector MUST differ from the requester, so a principal cannot decide their own
    // elevation. Compared ordinally on the trusted request PrincipalId.
    public static bool CheckerDiffersFromRequester(string requesterId, string checkerId) =>
        !string.Equals(requesterId, checkerId, StringComparison.Ordinal);

    // The effective JIT lifetime: the requested duration when it is a positive override,
    // otherwise the package default. A non-positive or absent request falls back to the
    // package default rather than issuing a zero-length (already-expired) grant.
    public static int EffectiveDurationMinutes(int? requestedMinutes, int packageDefaultMinutes) =>
        requestedMinutes is > 0 ? requestedMinutes.Value : packageDefaultMinutes;

    // Expiry instant for a grant issued at grantedAt for the given effective duration.
    public static DateTimeOffset ComputeExpiry(DateTimeOffset grantedAt, int durationMinutes) =>
        grantedAt.AddMinutes(durationMinutes);
}
