using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// An offline test double for the OpenFGA forward-Check seam (LRN-038): it forces allowed=true/false
// (or a thrown engine error) with NO live server, so OpenFgaProvider's permit/deny DecisionExplanation
// is unit-testable. It also records each call so a test can assert the provider forwarded the mapped
// (user, relation, object) — and that a fail-closed boundary deny never reaches the engine at all.
internal sealed class FakeOpenFgaCheckClient : IOpenFgaCheckClient
{
    private readonly bool _allowed;
    private readonly Exception? _toThrow;

    private FakeOpenFgaCheckClient(bool allowed, Exception? toThrow)
    {
        _allowed = allowed;
        _toThrow = toThrow;
    }

    // Always returns allowed=true — the permit path.
    public static FakeOpenFgaCheckClient Allowing() => new(true, null);

    // Always returns allowed=false — the ordinary ReBAC deny (no relationship path).
    public static FakeOpenFgaCheckClient Denying() => new(false, null);

    // Fails the check with an engine error, mimicking an unreachable/misbehaving server so the
    // provider's fail-closed catch is exercised without a live server.
    public static FakeOpenFgaCheckClient Throwing(Exception toThrow) => new(false, toThrow);

    public int Calls { get; private set; }

    public string? LastUser { get; private set; }

    public string? LastRelation { get; private set; }

    public string? LastObject { get; private set; }

    public Task<bool> CheckAsync(
        string user, string relation, string @object, CancellationToken cancellationToken = default)
    {
        Calls++;
        LastUser = user;
        LastRelation = relation;
        LastObject = @object;

        // A faulted Task mirrors the async SDK surfacing an engine error; the provider bridges with
        // GetAwaiter().GetResult(), so this throws exactly where a live Check would.
        return _toThrow is not null ? Task.FromException<bool>(_toThrow) : Task.FromResult(_allowed);
    }
}
