using AuthzEntitlements.Authz.Pdp.Audit;
using AuthzEntitlements.Authz.Pdp.Contracts;
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

        services.AddSingleton<AuthorizationDecisionProviderFactory>();
        services.AddSingleton<IPdpDecisionAuditSink, LoggingPdpDecisionAuditSink>();
        services.AddSingleton<PdpDecisionService>();

        return services;
    }
}
