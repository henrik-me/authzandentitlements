using AuthzEntitlements.Authz.Pdp.Audit;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using AuthzEntitlements.Authz.Pdp.Services;

namespace AuthzEntitlements.Authz.Pdp.Providers;

// Registers the PDP composition root: bind PdpOptions, register every engine as an
// IAuthorizationDecisionProvider (CS06-CS09 add their adapters here alongside the
// reference engine), the name-based selection factory, the audit sink, and the decision
// service that wraps the selected provider with telemetry + audit hooks.
public static class PdpServiceCollectionExtensions
{
    public static IServiceCollection AddPdp(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PdpOptions>(configuration.GetSection(PdpOptions.SectionName));

        services.AddSingleton<IAuthorizationDecisionProvider, ReferenceDecisionProvider>();

        // CS07 — OpenFGA (ReBAC) adapter. Bound + registered unconditionally so the factory can
        // select it by name, but its live client is built lazily (only on first use) so registration
        // never needs a running server: the default deterministic run stays engine-free. The service
        // is a singleton so the store/model bootstrap runs once and its ids are cached.
        services.Configure<OpenFgaOptions>(configuration.GetSection(OpenFgaOptions.SectionName));
        services.AddSingleton<OpenFgaRebacService>();
        services.AddSingleton<IAuthorizationDecisionProvider, OpenFgaProvider>();

        services.AddSingleton<AuthorizationDecisionProviderFactory>();
        services.AddSingleton<IPdpDecisionAuditSink, LoggingPdpDecisionAuditSink>();
        services.AddSingleton<PdpDecisionService>();

        return services;
    }
}
