using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Small hand-rolled builders that keep the rule-branch tests readable: each test builds
// exactly the AccessRequest shape it needs without repeating the record boilerplate. No
// framework magic — plain static factory methods over the public contract types.
internal static class PdpRequests
{
    public const string Contoso = "CONTOSO";
    public const string Fabrikam = "FABRIKAM";

    public static Subject User(string id, string? tenant, params string[] roles) =>
        new("user", id, roles, tenant);

    public static AccessRequest For(
        Subject subject,
        string action,
        Resource resource,
        params string[] scopes) =>
        new(subject, new ActionRequest(action), resource, new EvaluationContext(scopes));
}
