namespace AuthzEntitlements.Governance.Service.Domain;

// The active, time-bound grant created when an access request is approved. Roles is a
// snapshot of the package's roles at grant time (so later package edits do not
// retroactively change an issued grant). Expiry is enforced at read time via IsActive —
// an expired-but-not-revoked grant simply stops being active, so no background sweeper is
// needed.
public sealed class AccessGrant
{
    public Guid Id { get; set; }
    public Guid RequestId { get; set; }
    public string PrincipalId { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;
    public string AccessPackageCode { get; set; } = string.Empty;

    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedBy { get; set; }

    public ICollection<AccessGrantRole> Roles { get; } = [];

    // A grant is active only while it is neither revoked nor past its expiry. This is the
    // single definition of "currently in effect" used by the effective-roles and
    // active-grants reads.
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;
}
