using AuthzEntitlements.Authz.Pdp.Audit;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Opa;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using AuthzEntitlements.Authz.Pdp.Services;
using Microsoft.Extensions.Options;

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
        services.AddSingleton<IAuthorizationDecisionProvider, Adapters.AspNetCore.AspNetCorePolicyProvider>();
        services.AddSingleton<IAuthorizationDecisionProvider, Adapters.Casbin.CasbinDecisionProvider>();

        // CS07 — OpenFGA (ReBAC) adapter. Bound + registered unconditionally so the factory can
        // select it by name, but its live client is built lazily (only on first use) so registration
        // never needs a running server: the default deterministic run stays engine-free. The service
        // is a singleton so the store/model bootstrap runs once and its ids are cached. It is also
        // exposed as IOpenFgaCheckClient — the narrow forward-Check seam OpenFgaProvider depends on
        // (LRN-038) — resolved from the SAME singleton so the provider and the reverse-index
        // RebacEndpoints (which inject the concrete service) share one bootstrap.
        services.Configure<OpenFgaOptions>(configuration.GetSection(OpenFgaOptions.SectionName));
        services.AddSingleton<OpenFgaRebacService>();
        services.AddSingleton<IOpenFgaCheckClient>(sp => sp.GetRequiredService<OpenFgaRebacService>());
        services.AddSingleton<IAuthorizationDecisionProvider, OpenFgaProvider>();

        // Cedar adapter (CS09): a genuine in-process Cedar policy engine (MonoCloud.Cedar native
        // bindings) that owns the FULL fintech decision natively — the head-to-head with OPA. No
        // HttpClient/options needed (in-process). Selection stays config-driven: "Pdp:Provider"
        // defaults to "reference", so registering this adapter does not change the active engine.
        services.AddSingleton<IAuthorizationDecisionProvider, Adapters.Cedar.CedarDecisionProvider>();

        // OPA adapter (CS08): an out-of-process Rego engine reached over its REST data API. Bind
        // its options, register a named HttpClient (base address + timeout from config), and add
        // the provider. Selection stays config-driven — "Pdp:Provider" defaults to "reference", so
        // registering this adapter does not require a live OPA for builds/tests/`aspire run`.
        services.Configure<OpaOptions>(configuration.GetSection(OpaOptions.SectionName));
        services.AddHttpClient(OpaDecisionProvider.HttpClientName, (sp, client) =>
        {
            var opa = sp.GetRequiredService<IOptions<OpaOptions>>().Value;
            client.BaseAddress = new Uri(opa.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(opa.TimeoutSeconds);
        });
        services.AddSingleton<IAuthorizationDecisionProvider, Adapters.Opa.OpaDecisionProvider>();

        services.AddSingleton<AuthorizationDecisionProviderFactory>();
        services.AddPdpDecisionAuditSink(configuration);
        services.AddSingleton<PdpDecisionService>();

        return services;
    }
}
