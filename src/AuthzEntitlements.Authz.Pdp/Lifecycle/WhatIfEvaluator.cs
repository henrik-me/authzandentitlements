using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;

namespace AuthzEntitlements.Authz.Pdp.Lifecycle;

// What-if simulation (CS17): evaluate a hypothetical AccessRequest against a chosen engine (or
// the active one) and return the full, self-explaining decision. This is a PREVIEW, not an
// enforced decision — it deliberately does NOT go through PdpDecisionService, so a simulation
// never emits a real authorization-audit event or decision metric that downstream consumers
// (CS13 audit pipeline) would mistake for a live enforcement. Authors use it to see exactly what
// a policy/engine would decide before promoting a change.
public sealed class WhatIfEvaluator
{
    private readonly AuthorizationDecisionProviderFactory _factory;

    public WhatIfEvaluator(AuthorizationDecisionProviderFactory factory) => _factory = factory;

    // Evaluate against the named engine, or the active engine when the name is blank. Resolution
    // fails closed (a non-blank unknown engine throws via the factory); the request-boundary caller
    // guards unknown names with TryGetProvider first so a bad name is a 400, not a 500.
    public WhatIfResult Evaluate(string? engine, AccessRequest request)
    {
        var provider = string.IsNullOrWhiteSpace(engine)
            ? _factory.GetActiveProvider()
            : _factory.GetProvider(engine);

        var decision = provider.Evaluate(request);
        return new WhatIfResult(provider.Name, decision.Decision, decision.Reasons, decision.Obligations);
    }
}
