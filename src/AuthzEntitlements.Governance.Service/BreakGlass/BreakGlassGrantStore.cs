using AuthzEntitlements.Governance.Service.Domain;

namespace AuthzEntitlements.Governance.Service.BreakGlass;

// Thread-safe, in-memory system-of-record for break-glass grants, registered as a singleton.
// Deliberately in-memory (no EF/Postgres) to preserve the deterministic no-Docker path — EF
// persistence is a documented follow-up. A single monitor lock guards every operation so each
// read returns a consistent snapshot and each check-then-set transition (Issue, Review) is
// atomic. Every mutation is FAIL-CLOSED: blank/invalid input and illegal state transitions
// throw rather than silently accepting bad state, so the endpoint layer maps them to a clear
// 400/404/409 instead of a silent default.
public sealed class BreakGlassGrantStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, BreakGlassGrant> _grants = [];

    // Issues a new emergency grant covering a single action for one principal. ExpiresAt is
    // GrantedAt + durationMinutes; expiry is then enforced at read time via IsActive. Rejects
    // blank subject/tenant/action/justification and a non-positive duration.
    public BreakGlassGrant Issue(
        string principalId,
        string tenantCode,
        string action,
        string justification,
        int durationMinutes,
        DateTimeOffset now)
    {
        Require(principalId, nameof(principalId));
        Require(tenantCode, nameof(tenantCode));
        Require(action, nameof(action));
        Require(justification, nameof(justification));
        if (durationMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationMinutes), durationMinutes, "durationMinutes must be positive");
        }

        var grant = new BreakGlassGrant
        {
            Id = Guid.NewGuid(),
            PrincipalId = principalId,
            TenantCode = tenantCode,
            Action = action,
            Justification = justification,
            GrantedAt = now,
            ExpiresAt = now.AddMinutes(durationMinutes),
        };

        lock (_gate)
        {
            _grants[grant.Id] = grant;
        }

        return Copy(grant);
    }

    public BreakGlassGrant? Get(Guid id)
    {
        lock (_gate)
        {
            return _grants.TryGetValue(id, out var grant) ? Copy(grant) : null;
        }
    }

    public IReadOnlyList<BreakGlassGrant> ListAll() => Snapshot(static (_, _) => true, DateTimeOffset.MinValue);

    public IReadOnlyList<BreakGlassGrant> ListActive(DateTimeOffset now) =>
        Snapshot(static (g, at) => g.IsActive(at), now);

    // Grants whose mandatory post-review is still outstanding: past their window (used/expired)
    // and not yet reviewed. This is what a reviewer polls to satisfy the break-glass control.
    public IReadOnlyList<BreakGlassGrant> ListRequiringReview(DateTimeOffset now) =>
        Snapshot(static (g, at) => g.RequiresReview(at), now);

    // Records the mandatory post-review. Fail-closed: unknown id throws KeyNotFoundException
    // (endpoint -> 404), a blank reviewer/outcome throws ArgumentException (400), reviewing a grant
    // that is still active (now < ExpiresAt) throws InvalidOperationException (409) so the mandatory
    // review is genuinely POST-expiry and cannot be pre-empted while the grant is live, and a second
    // review of an already-reviewed grant throws InvalidOperationException (409) rather than
    // silently overwriting the first, accountable review.
    public BreakGlassGrant Review(Guid id, string reviewedBy, string outcome, DateTimeOffset now)
    {
        Require(reviewedBy, nameof(reviewedBy));
        Require(outcome, nameof(outcome));

        lock (_gate)
        {
            if (!_grants.TryGetValue(id, out var grant))
            {
                throw new KeyNotFoundException($"unknown break-glass grant '{id}'");
            }

            if (grant.IsActive(now))
            {
                throw new InvalidOperationException(
                    "break-glass grant cannot be reviewed before it expires (mandatory post-review)");
            }

            if (grant.ReviewedAt is not null)
            {
                throw new InvalidOperationException("break-glass grant is already reviewed");
            }

            grant.ReviewedAt = now;
            grant.ReviewedBy = reviewedBy;
            grant.ReviewOutcome = outcome;
            return Copy(grant);
        }
    }

    private IReadOnlyList<BreakGlassGrant> Snapshot(
        Func<BreakGlassGrant, DateTimeOffset, bool> predicate, DateTimeOffset now)
    {
        lock (_gate)
        {
            return _grants.Values
                .Where(g => predicate(g, now))
                .OrderByDescending(g => g.GrantedAt)
                .Select(Copy)
                .ToList();
        }
    }

    // Returns a defensive copy so a caller can never mutate the store's internal grant (the domain
    // type has public setters) or observe a torn read while a transition is in flight.
    private static BreakGlassGrant Copy(BreakGlassGrant g) => new()
    {
        Id = g.Id,
        PrincipalId = g.PrincipalId,
        TenantCode = g.TenantCode,
        Action = g.Action,
        Justification = g.Justification,
        GrantedAt = g.GrantedAt,
        ExpiresAt = g.ExpiresAt,
        ReviewedAt = g.ReviewedAt,
        ReviewedBy = g.ReviewedBy,
        ReviewOutcome = g.ReviewOutcome,
    };

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required", name);
        }
    }
}
