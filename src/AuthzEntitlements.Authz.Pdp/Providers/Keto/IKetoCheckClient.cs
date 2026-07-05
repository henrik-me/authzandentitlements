namespace AuthzEntitlements.Authz.Pdp.Providers.Keto;

// The narrow forward-check seam KetoProvider depends on (LRN-038), mirroring ISpiceDbCheckClient and
// IOpenFgaCheckClient. KetoCheckService is the production implementation — a sealed adapter over the
// live Keto REST clients whose blank-endpoint path throws before any check — so a permit/deny
// DecisionExplanation could not be asserted in the OFFLINE suite. Extracting this one-member
// interface lets a test double force allowed=true/false with no server, so the ReBAC permit/deny
// explanation (engine=keto, DeterminingRule=relationship, the relationship-tuple reference) is
// unit-testable offline.
public interface IKetoCheckClient
{
    // Forward check: does user:subjectId have <permission> on account:accountId? Ids are BARE (no
    // "type:" prefix) — Keto takes the object namespace and id separately and the subject as a bare
    // subject_id, so the fixed "account" namespace / "user" subject convention is supplied by the
    // service. Bootstraps the seed relationships on first use (idempotent) and returns allowed=false
    // on a negative check.
    Task<bool> CheckAsync(
        string subjectId, string permission, string accountId, CancellationToken cancellationToken = default);
}
