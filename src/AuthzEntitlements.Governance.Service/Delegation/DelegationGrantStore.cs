using AuthzEntitlements.Governance.Service.Domain;

namespace AuthzEntitlements.Governance.Service.Delegation;

// Thread-safe, in-memory system-of-record for manager->delegate delegation grants, registered
// as a singleton. Kept in-memory to avoid adding a NEW EF entity/migration/table for these
// ephemeral grants — NOT because the service avoids Postgres: Governance.Service still requires
// Postgres at startup for its durable entitlement grants. Persisting delegation grants (mirroring
// the durable AccessGrant tables) is a documented follow-up; they do not survive a restart today.
// A single monitor lock guards every operation so reads return a consistent snapshot and the
// revoke check-then-set is atomic. Every mutation is FAIL-CLOSED: blank/invalid input and illegal
// transitions throw so the endpoint layer maps them to a clear 400/404/409 instead of a silent
// default.
public sealed class DelegationGrantStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, DelegationGrant> _grants = [];
    private readonly int _maxGrants;

    // Bounded-retention cap. There is no background sweeper, so the store is capped on write:
    // once the map exceeds _maxGrants, the least-valuable grants are evicted (terminal ones —
    // revoked or expired — oldest-first, then the oldest still-active grant). This keeps the
    // anonymous ListAll() footprint bounded (memory / payload-size / DoS guard). The DI singleton
    // uses the parameterless default; tests can pass a small cap.
    public DelegationGrantStore(int maxGrants = 5000)
    {
        if (maxGrants <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxGrants), maxGrants, "maxGrants must be positive");
        }

        _maxGrants = maxGrants;
    }

    // Creates a delegation authorising delegateId to act on behalf of managerId for the given
    // scopes until now + durationMinutes. Rejects blank ids/tenant, a non-positive duration, a
    // self-delegation (manager == delegate is meaningless and treated as bad input), and an
    // empty scope set (a delegation that grants nothing). Scopes are trimmed, de-duplicated and
    // blank entries dropped so a caller cannot smuggle empty or duplicate capability tokens.
    public DelegationGrant Create(
        string managerId,
        string delegateId,
        string tenantCode,
        IEnumerable<string> scopes,
        int durationMinutes,
        DateTimeOffset now)
    {
        Require(managerId, nameof(managerId));
        Require(delegateId, nameof(delegateId));
        Require(tenantCode, nameof(tenantCode));
        if (string.Equals(managerId, delegateId, StringComparison.Ordinal))
        {
            throw new ArgumentException("delegateId must differ from managerId", nameof(delegateId));
        }

        if (durationMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationMinutes), durationMinutes, "durationMinutes must be positive");
        }

        var normalized = NormalizeScopes(scopes);
        if (normalized.Count == 0)
        {
            throw new ArgumentException("at least one non-blank scope is required", nameof(scopes));
        }

        var grant = new DelegationGrant
        {
            Id = Guid.NewGuid(),
            ManagerId = managerId,
            DelegateId = delegateId,
            TenantCode = tenantCode,
            Scopes = normalized,
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

    public DelegationGrant? Get(Guid id)
    {
        lock (_gate)
        {
            return _grants.TryGetValue(id, out var grant) ? Copy(grant) : null;
        }
    }

    public IReadOnlyList<DelegationGrant> ListAll() => Snapshot(static (_, _) => true, DateTimeOffset.MinValue);

    public IReadOnlyList<DelegationGrant> ListActive(DateTimeOffset now) =>
        Snapshot(static (g, at) => g.IsActive(at), now);

    // Revokes an active delegation. Fail-closed: unknown id throws KeyNotFoundException
    // (endpoint -> 404), a blank revoker throws ArgumentException (400), and revoking an
    // already-revoked grant throws InvalidOperationException (409) rather than silently
    // re-stamping the revocation over stale state.
    public DelegationGrant Revoke(Guid id, string revokedBy, DateTimeOffset now)
    {
        Require(revokedBy, nameof(revokedBy));

        lock (_gate)
        {
            if (!_grants.TryGetValue(id, out var grant))
            {
                throw new KeyNotFoundException($"unknown delegation grant '{id}'");
            }

            if (grant.RevokedAt is not null)
            {
                throw new InvalidOperationException("delegation grant is already revoked");
            }

            grant.RevokedAt = now;
            grant.RevokedBy = revokedBy;
            return Copy(grant);
        }
    }

    // Enforces the bounded-retention cap; MUST be called under _gate. Evicts (Count - _maxGrants)
    // grants, taking terminal grants (revoked or expired at 'now') before still-active ones and,
    // within each group, the oldest by GrantedAt first. No-op while at or under the cap. Uses the
    // same 'now' as the triggering write so "terminal" matches read-time expiry semantics.
    private void EvictIfOverCap(DateTimeOffset now)
    {
        var overflow = _grants.Count - _maxGrants;
        if (overflow <= 0)
        {
            return;
        }

        var victims = _grants.Values
            .OrderBy(g => IsTerminal(g, now) ? 0 : 1)
            .ThenBy(g => g.GrantedAt)
            .Take(overflow)
            .Select(g => g.Id)
            .ToList();

        foreach (var id in victims)
        {
            _grants.Remove(id);
        }
    }

    // Terminal = lifecycle over: revoked, or past its expiry window. These carry the least
    // residual value, so they are evicted first when the store is over its cap.
    private static bool IsTerminal(DelegationGrant grant, DateTimeOffset now) =>
        grant.RevokedAt is not null || now >= grant.ExpiresAt;

    private IReadOnlyList<DelegationGrant> Snapshot(
        Func<DelegationGrant, DateTimeOffset, bool> predicate, DateTimeOffset now)
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

    // Returns a defensive copy (Scopes cloned into a new list) so a caller can never mutate the
    // store's internal grant (the domain type has public setters) or observe a torn read while a
    // transition is in flight.
    private static DelegationGrant Copy(DelegationGrant g) => new()
    {
        Id = g.Id,
        ManagerId = g.ManagerId,
        DelegateId = g.DelegateId,
        TenantCode = g.TenantCode,
        Scopes = [.. g.Scopes],
        GrantedAt = g.GrantedAt,
        ExpiresAt = g.ExpiresAt,
        RevokedAt = g.RevokedAt,
        RevokedBy = g.RevokedBy,
    };

    private static IReadOnlyList<string> NormalizeScopes(IEnumerable<string> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in scopes)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var trimmed = raw.Trim();
            if (seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        return result;
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required", name);
        }
    }
}
