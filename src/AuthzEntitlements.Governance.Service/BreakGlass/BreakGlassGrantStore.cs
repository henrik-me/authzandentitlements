using AuthzEntitlements.Governance.Service.Domain;

namespace AuthzEntitlements.Governance.Service.BreakGlass;

// Thread-safe, in-memory system-of-record for break-glass grants, registered as a singleton.
// Kept in-memory to avoid adding a NEW EF entity/migration/table for these ephemeral emergency
// grants — NOT because the service avoids Postgres: Governance.Service still requires Postgres at
// startup for its durable entitlement grants. Persisting break-glass grants (mirroring the durable
// AccessGrant tables) is a documented follow-up; they do not survive a restart today. A single
// monitor lock guards every operation so each read returns a consistent snapshot and each
// check-then-set transition (Issue, Review) is atomic. Every mutation is FAIL-CLOSED: blank/invalid
// input and illegal state transitions throw rather than silently accepting bad state, so the
// endpoint layer maps them to a clear 400/404/409 instead of a silent default.
public sealed class BreakGlassGrantStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, BreakGlassGrant> _grants = [];
    private readonly int _maxGrants;

    // Bounded-retention cap. There is no background sweeper, so the store is capped on write: once the
    // map exceeds _maxGrants, grants are evicted by EvictionRank (reviewed first, then still-active, and
    // expired-but-UNREVIEWED grants LAST — those still owe a mandatory post-review), oldest-first within
    // each rank. This keeps the anonymous ListAll() footprint bounded (memory / payload-size / DoS guard)
    // while never silently dropping a grant that still requires review. The DI singleton uses the
    // parameterless default; tests can pass a small cap.
    public BreakGlassGrantStore(int maxGrants = 5000)
    {
        if (maxGrants <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxGrants), maxGrants, "maxGrants must be positive");
        }

        _maxGrants = maxGrants;
    }

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
        principalId = Require(principalId, nameof(principalId));
        tenantCode = Require(tenantCode, nameof(tenantCode));
        action = Require(action, nameof(action));
        justification = Require(justification, nameof(justification));
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
            EvictIfOverCap(now);
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
        reviewedBy = Require(reviewedBy, nameof(reviewedBy));
        outcome = Require(outcome, nameof(outcome));

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

    // Enforces the bounded-retention cap; MUST be called under _gate. Evicts (Count - _maxGrants)
    // grants in ascending EvictionRank (reviewed first, then still-active, and expired-but-UNREVIEWED
    // grants LAST) and, within each rank, the oldest by GrantedAt first. No-op while at or under the
    // cap. Uses the same 'now' as the triggering write so expiry matches read-time semantics.
    private void EvictIfOverCap(DateTimeOffset now)
    {
        var overflow = _grants.Count - _maxGrants;
        if (overflow <= 0)
        {
            return;
        }

        var victims = _grants.Values
            .OrderBy(g => EvictionRank(g, now))
            .ThenBy(g => g.GrantedAt)
            .Take(overflow)
            .Select(g => g.Id)
            .ToList();

        foreach (var id in victims)
        {
            _grants.Remove(id);
        }
    }

    // Eviction priority (lower = evicted first). A REVIEWED grant is lifecycle-complete, so it carries
    // the least residual value. A still-ACTIVE grant may yet be used. An EXPIRED-but-UNREVIEWED grant
    // still owes its mandatory post-review (it is in the pending-review queue and holds the audit
    // correlation an operator must close out), so it is preserved LONGEST — evicted only as a last
    // resort so the retention cap can never silently drop a grant that still requires review.
    private static int EvictionRank(BreakGlassGrant grant, DateTimeOffset now) =>
        grant.ReviewedAt is not null ? 0   // reviewed => lifecycle-complete, evict first
        : now < grant.ExpiresAt ? 1        // still active
        : 2;                               // expired + unreviewed => owes mandatory review, evict last

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

    // Validates a required string is non-blank and returns it TRIMMED, so stored ids/fields are
    // normalized: accidental leading/trailing whitespace never persists or skews the Ordinal matching
    // the PDP + store rely on (e.g. "user-1 " would otherwise never match "user-1").
    private static string Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required", name);
        }

        return value.Trim();
    }
}
