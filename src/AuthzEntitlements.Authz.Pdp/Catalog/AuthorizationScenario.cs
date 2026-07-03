using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Catalog;

// One concrete fintech authorization scenario expressed engine-agnostically: a fully
// built AccessRequest plus the expected decision and primary reason code. The same
// scenario dispatches unchanged to any provider, so engines are compared apples-to-apples.
public sealed record AuthorizationScenario(
    string Id,
    string Description,
    AccessRequest Request,
    Decision Expected,
    string ExpectedReasonCode);
