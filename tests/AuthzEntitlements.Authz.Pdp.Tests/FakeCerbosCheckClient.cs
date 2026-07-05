using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Cerbos;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// An offline test double for the Cerbos forward-decision seam (LRN-038), mirroring
// FakeSpiceDbCheckClient: it forces any (allowed, outputToken) outcome — or a thrown engine error —
// with NO live Cerbos server, so CerbosDecisionProvider's full-decision reason/obligation mapping,
// its CS16 explanation, and its fail-closed catch are all unit-testable. It also records each call so
// a test can assert the provider forwarded the request unchanged.
internal sealed class FakeCerbosCheckClient : ICerbosCheckClient
{
    private readonly CerbosCheckOutcome? _outcome;
    private readonly Exception? _toThrow;

    private FakeCerbosCheckClient(CerbosCheckOutcome? outcome, Exception? toThrow)
    {
        _outcome = outcome;
        _toThrow = toThrow;
    }

    // Returns a fixed (allowed, outputToken) outcome — the shape the live service extracts from a
    // Cerbos CheckResources reply.
    public static FakeCerbosCheckClient Returning(bool allowed, string? outputToken) =>
        new(new CerbosCheckOutcome(allowed, outputToken), null);

    // Fails the check with an engine error, mimicking an unreachable/misconfigured server so the
    // provider's fail-closed catch is exercised without a live server.
    public static FakeCerbosCheckClient Throwing(Exception toThrow) => new(null, toThrow);

    public int Calls { get; private set; }

    public AccessRequest? LastRequest { get; private set; }

    public CerbosCheckOutcome Check(AccessRequest request)
    {
        Calls++;
        LastRequest = request;

        if (_toThrow is not null)
        {
            throw _toThrow;
        }

        return _outcome!;
    }
}
