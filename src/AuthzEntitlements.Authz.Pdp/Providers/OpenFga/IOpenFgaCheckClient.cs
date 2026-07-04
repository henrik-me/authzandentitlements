namespace AuthzEntitlements.Authz.Pdp.Providers.OpenFga;

// The narrow forward-Check seam OpenFgaProvider depends on (LRN-038). OpenFgaRebacService is the
// production implementation — a sealed adapter over the live OpenFGA client whose blank-ApiUrl path
// throws before any Check — so a permit/deny DecisionExplanation could not be asserted in the
// OFFLINE suite. Extracting this one-member interface lets a test double force allowed=true/false
// with no server, so the ReBAC permit/deny explanation (engine=openfga, DeterminingRule=relationship,
// the relationship-tuple reference) is unit-testable offline. Only the member Evaluate needs is
// exposed; the reverse-index queries (WhoCanAccess / WhatCanUserAccess) stay on the concrete service,
// which the RebacEndpoints consume directly.
public interface IOpenFgaCheckClient
{
    // Forward check: does user have relation on object? object is a full "type:id" string. Bootstraps
    // the store/model/tuples on first use (idempotent) and returns allowed=false on a negative check.
    Task<bool> CheckAsync(
        string user, string relation, string @object, CancellationToken cancellationToken = default);
}
