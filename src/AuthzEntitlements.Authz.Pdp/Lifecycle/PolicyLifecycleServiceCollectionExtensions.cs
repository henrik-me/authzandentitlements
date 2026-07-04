using AuthzEntitlements.Authz.Pdp.Lifecycle;

namespace AuthzEntitlements.Authz.Pdp.Lifecycle;

// Registers the CS17 policy-lifecycle services: the shadow / dual-run comparison harness and the
// what-if simulator. Both are stateless singletons layered over the AuthorizationDecisionProviderFactory,
// so they add no external dependency and do not change the deterministic default run. Call AFTER
// AddPdp so the factory and every engine provider are already registered.
public static class PolicyLifecycleServiceCollectionExtensions
{
    public static IServiceCollection AddPolicyLifecycle(this IServiceCollection services)
    {
        services.AddSingleton<ShadowRunner>();
        services.AddSingleton<WhatIfEvaluator>();
        return services;
    }
}
