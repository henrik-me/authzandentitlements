using AuthzEntitlements.Authz.Pdp.Providers.SpiceDb;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// An offline test double for the SpiceDB forward-check seam (LRN-038), mirroring
// FakeOpenFgaCheckClient: it forces allowed=true/false (or a thrown engine error) with NO live
// server, so SpiceDbProvider's permit/deny DecisionExplanation is unit-testable. It also records
// each call so a test can assert the provider forwarded the mapped (subjectId, permission, accountId)
// — and that a fail-closed boundary deny never reaches the engine at all.
internal sealed class FakeSpiceDbCheckClient : ISpiceDbCheckClient
{
    private readonly bool _allowed;
    private readonly Exception? _toThrow;

    private FakeSpiceDbCheckClient(bool allowed, Exception? toThrow)
    {
        _allowed = allowed;
        _toThrow = toThrow;
    }

    // Always returns allowed=true — the permit path.
    public static FakeSpiceDbCheckClient Allowing() => new(true, null);

    // Always returns allowed=false — the ordinary ReBAC deny (no relationship path).
    public static FakeSpiceDbCheckClient Denying() => new(false, null);

    // Fails the check with an engine error, mimicking an unreachable/misbehaving server so the
    // provider's fail-closed catch is exercised without a live server.
    public static FakeSpiceDbCheckClient Throwing(Exception toThrow) => new(false, toThrow);

    public int Calls { get; private set; }

    public string? LastSubjectId { get; private set; }

    public string? LastPermission { get; private set; }

    public string? LastAccountId { get; private set; }

    public Task<bool> CheckAsync(
        string subjectId, string permission, string accountId, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastSubjectId = subjectId;
        LastPermission = permission;
        LastAccountId = accountId;

        // A faulted Task mirrors the async gRPC client surfacing an engine error; the provider bridges
        // with GetAwaiter().GetResult(), so this throws exactly where a live check would.
        return _toThrow is not null ? Task.FromException<bool>(_toThrow) : Task.FromResult(_allowed);
    }
}
