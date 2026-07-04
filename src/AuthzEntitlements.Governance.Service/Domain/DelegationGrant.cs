namespace AuthzEntitlements.Governance.Service.Domain;

// A manager->delegate delegation grant: authorises DelegateId to act on behalf of ManagerId
// for the given capability scopes until it expires or is explicitly revoked. Mirrors
// AccessGrant's read-time expiry — IsActive(now) is the single definition of "currently in
// effect" used by the active-delegation reads, so an expired-but-not-revoked grant just
// stops being active with no background sweeper.
public sealed class DelegationGrant
{
    public Guid Id { get; set; }
    public string ManagerId { get; set; } = string.Empty;
    public string DelegateId { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;

    // The delegated agent.bank.* capability scopes granted to the delegate. Snapshot at
    // create time so later edits elsewhere never retroactively widen an issued delegation.
    public IReadOnlyList<string> Scopes { get; set; } = [];

    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }

    // Active only while it is neither revoked nor past its expiry. The boundary is exclusive
    // — now == ExpiresAt is already inactive — matching AccessGrant.IsActive.
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;
}
