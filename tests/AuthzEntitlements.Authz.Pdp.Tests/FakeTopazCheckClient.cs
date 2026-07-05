using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Topaz;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// An offline test double for the Topaz forward-decision seam (LRN-038), mirroring FakeCerbosCheckClient:
// it forces any raw Rego decision object (decision/reason/rule/obligations) — or a thrown engine error —
// with NO live Topaz authorizer, so TopazDecisionProvider's full-decision reason/obligation mapping, its
// CS16 explanation, and its fail-closed catch are all unit-testable. It also records each call so a test
// can assert the provider forwarded the request unchanged.
internal sealed class FakeTopazCheckClient : ITopazCheckClient
{
    private readonly TopazCheckOutcome? _outcome;
    private readonly Exception? _toThrow;

    private FakeTopazCheckClient(TopazCheckOutcome? outcome, Exception? toThrow)
    {
        _outcome = outcome;
        _toThrow = toThrow;
    }

    // Returns a decision object with the given raw fields — the shape the live service extracts from the
    // authorizer's Query result binding. Obligations default to null (an absent obligations field), the
    // legitimate no-obligation permit.
    public static FakeTopazCheckClient Returning(
        string? decision, string? reason, string? rule = null, IReadOnlyList<string>? obligations = null) =>
        new(new TopazCheckOutcome(decision, reason, rule, obligations), null);

    // Returns a specific outcome instance — used to force the None sentinel (an empty/malformed query
    // result) so the provider's absent-decision fail-closed path is exercised.
    public static FakeTopazCheckClient ReturningOutcome(TopazCheckOutcome outcome) => new(outcome, null);

    // Fails the check with an engine error, mimicking an unreachable/misconfigured authorizer so the
    // provider's fail-closed catch is exercised without a live server.
    public static FakeTopazCheckClient Throwing(Exception toThrow) => new(null, toThrow);

    public int Calls { get; private set; }

    public AccessRequest? LastRequest { get; private set; }

    public TopazCheckOutcome Check(AccessRequest request)
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
