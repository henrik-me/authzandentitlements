namespace AuthzEntitlements.Authz.Pdp.Providers.SpiceDb;

// The narrow forward-Check seam SpiceDbProvider depends on (LRN-038), mirroring IOpenFgaCheckClient.
// SpiceDbCheckService is the production implementation — a sealed adapter over the live SpiceDB gRPC
// client whose blank-Endpoint path throws before any check — so a permit/deny DecisionExplanation
// could not be asserted in the OFFLINE suite. Extracting this one-member interface lets a test double
// force allowed=true/false with no server, so the ReBAC permit/deny explanation (engine=spicedb,
// DeterminingRule=relationship, the relationship-tuple reference) is unit-testable offline.
public interface ISpiceDbCheckClient
{
    // Forward check: does user:subjectId have <permission> on account:accountId? Ids are BARE (no
    // "type:" prefix) — SpiceDB takes the object type and id separately, so the fixed "user"/"account"
    // types are supplied by the service. Bootstraps the schema + seed relationships on first use
    // (idempotent) and returns allowed=false on a negative check.
    Task<bool> CheckAsync(
        string subjectId, string permission, string accountId, CancellationToken cancellationToken = default);
}
