using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.AspNetCore;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Casbin;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Cedar;
using Microsoft.Extensions.Options;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Shared helpers for the CS17 policy-lifecycle tests: build a factory over the deterministic
// in-process RBAC engine family, instantiate an engine by name, and provide the two canonical
// permit/deny requests the shadow + what-if tests reuse.
internal static class LifecycleTestSupport
{
    public static readonly string[] RbacEngineNames = ["reference", "aspnet", "casbin", "cedar"];

    // The deterministic in-process RBAC engines — same set ShadowRunner.DeterministicRbacFamily
    // names, instantiated for direct use in tests.
    public static IAuthorizationDecisionProvider[] RbacProviders() =>
        RbacEngineNames.Select(ProviderByName).ToArray();

    public static IAuthorizationDecisionProvider ProviderByName(string name) => name switch
    {
        "reference" => new ReferenceDecisionProvider(),
        "aspnet" => new AspNetCorePolicyProvider(),
        "casbin" => new CasbinDecisionProvider(),
        "cedar" => new CedarDecisionProvider(),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unknown test engine"),
    };

    public static AuthorizationDecisionProviderFactory Factory(
        string active, params IAuthorizationDecisionProvider[] providers) =>
        new(providers, Options.Create(new PdpOptions { Provider = active }));

    public static AuthorizationDecisionProviderFactory RbacFactory(string active = "reference") =>
        Factory(active, RbacProviders());

    // Teller reads an account in ANOTHER tenant: a deny (TenantMismatch) every RBAC engine agrees on.
    public static AccessRequest DenyRequest() =>
        new(new Subject("user", "user-teller1", [RoleNames.Teller], "CONTOSO"),
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account", Tenant: "FABRIKAM"),
            new EvaluationContext([ScopeNames.Read]));

    // Teller creates a $15,000 transfer as themselves: a permit carrying the require_approval
    // threshold obligation, agreed on by every RBAC engine.
    public static AccessRequest PermitLargeTxn() =>
        new(new Subject("user", "user-teller1", [RoleNames.Teller], "CONTOSO"),
            new ActionRequest(ActionNames.TransactionCreate),
            new Resource("transaction", Tenant: "CONTOSO", Amount: 15_000m, MakerId: "user-teller1"),
            new EvaluationContext([ScopeNames.TransactionsWrite]));
}

// A provider that always returns a fixed decision — forces agreement/divergence in shadow and
// drift tests without depending on a real engine's rules.
internal sealed class FixedProvider(string name, AccessDecision decision) : IAuthorizationDecisionProvider
{
    public string Name => name;

    public AccessDecision Evaluate(AccessRequest request) => decision;
}
