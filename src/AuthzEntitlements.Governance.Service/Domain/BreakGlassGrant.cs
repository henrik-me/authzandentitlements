namespace AuthzEntitlements.Governance.Service.Domain;

// An emergency break-glass grant: a bounded, auto-expiring elevation that covers a single
// bank action for one principal. Like AccessGrant, expiry is enforced at read time via
// IsActive(now) — an expired grant simply stops being active, so no background sweeper is
// needed. A break-glass grant additionally carries a MANDATORY post-review: once its window
// closes it must be reviewed (RequiresReview), and the review outcome/actor are recorded on
// the grant itself so the emergency access always leaves an accountable trail.
public sealed class BreakGlassGrant
{
    public Guid Id { get; set; }
    public string PrincipalId { get; set; } = string.Empty;
    public string TenantCode { get; set; } = string.Empty;

    // The bank action class this emergency grant covers, e.g. "bank.transaction.create".
    public string Action { get; set; } = string.Empty;
    public string Justification { get; set; } = string.Empty;

    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    // Mandatory post-review state. All three stay null until a reviewer records the outcome
    // via BreakGlassGrantStore.Review.
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public string? ReviewOutcome { get; set; }

    // A break-glass grant is active only while it is still within its window. Unlike the
    // post-review gate, activity is NOT review-gated: an active grant is simply one whose
    // expiry has not yet passed. The boundary is exclusive — now == ExpiresAt is already
    // inactive — matching AccessGrant.IsActive.
    public bool IsActive(DateTimeOffset now) => now < ExpiresAt;

    // The mandatory post-review is outstanding once the grant has left its active window
    // (used/expired) and no reviewer has recorded an outcome yet. This is the control that
    // forces every emergency elevation to be reviewed after the fact.
    public bool RequiresReview(DateTimeOffset now) => now >= ExpiresAt && ReviewedAt is null;
}
