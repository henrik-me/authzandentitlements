using AuthzEntitlements.Authz.Pdp.Audit;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Cerbos;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Opa;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Topaz;
using AuthzEntitlements.Authz.Pdp.Providers.Keto;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using AuthzEntitlements.Authz.Pdp.Providers.SpiceDb;
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

        // CS26 — SpiceDB (ReBAC) adapter, the head-to-head counterpart to OpenFGA. Bound + registered
        // unconditionally so the factory can select it by name, but its live gRPC client is built lazily
        // (only on first use) so registration never needs a running server: the default deterministic
        // run stays engine-free. The service is a singleton so the schema/relationship bootstrap runs
        // once and the channel is cached. It is also exposed as ISpiceDbCheckClient — the narrow
        // forward-check seam SpiceDbProvider depends on (LRN-038) — resolved from the SAME singleton.
        services.Configure<SpiceDbOptions>(configuration.GetSection(SpiceDbOptions.SectionName));
        services.AddSingleton<SpiceDbCheckService>();
        services.AddSingleton<ISpiceDbCheckClient>(sp => sp.GetRequiredService<SpiceDbCheckService>());
        services.AddSingleton<IAuthorizationDecisionProvider, SpiceDbProvider>();

        // CS46 — Ory Keto (ReBAC) adapter, a head-to-head counterpart to SpiceDB and OpenFGA. Bound +
        // registered unconditionally so the factory can select it by name, but its live REST clients are
        // built lazily (only on first use) so registration never needs a running server: the default
        // deterministic run stays engine-free. The service is a singleton so the relationship bootstrap
        // runs once and the clients are cached. It is also exposed as IKetoCheckClient — the narrow
        // forward-check seam KetoProvider depends on (LRN-038) — resolved from the SAME singleton.
        services.Configure<KetoOptions>(configuration.GetSection(KetoOptions.SectionName));
        services.AddSingleton<KetoCheckService>();
        services.AddSingleton<IKetoCheckClient>(sp => sp.GetRequiredService<KetoCheckService>());
        services.AddSingleton<IAuthorizationDecisionProvider, KetoProvider>();

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

        // CS26 — Cerbos adapter: an out-of-process, full-decision PDP (the head-to-head with OPA)
        // reached over gRPC. Cerbos owns the WHOLE fintech decision natively in YAML/CEL policies
        // (infra/cerbos/policies), like OPA does in Rego. Bind its options + register the lazy gRPC
        // client and the provider. Selection stays config-driven — "Pdp:Provider" defaults to
        // "reference", so registering this adapter does not require a live Cerbos for
        // builds/tests/`aspire run`: the client is built lazily only on first actual check. The
        // service is a singleton so the client/channel is cached, and it is also exposed as
        // ICerbosCheckClient — the narrow forward-decision seam CerbosDecisionProvider depends on
        // (LRN-038) — resolved from the SAME singleton.
        services.Configure<CerbosOptions>(configuration.GetSection(CerbosOptions.SectionName));
        services.AddSingleton<CerbosCheckService>();
        services.AddSingleton<ICerbosCheckClient>(sp => sp.GetRequiredService<CerbosCheckService>());
        services.AddSingleton<IAuthorizationDecisionProvider, Adapters.Cerbos.CerbosDecisionProvider>();

        // CS46 — Topaz (Aserto) adapter: an out-of-process, full-decision PDP driven over its OPA policy
        // bundle. Topaz is OPA-based, so — unlike the ReBAC engines — it answers the WHOLE fintech
        // decision by evaluating the SAME Rego the OPA adapter uses (infra/opa/policy), reached through
        // the Aserto authorizer gRPC API. It is the head-to-head "OPA standalone vs OPA-inside-Topaz"
        // (Topaz's Zanzibar directory is deliberately NOT used for the decision — the documented parity
        // boundary). Bind its options + register the lazy authorizer client and the provider. Selection
        // stays config-driven — "Pdp:Provider" defaults to "reference", so registering this adapter does
        // not require a live Topaz for builds/tests/`aspire run`: the client is built lazily only on first
        // actual check. The service is a singleton so the channel is cached, and it is also exposed as
        // ITopazCheckClient — the narrow forward-decision seam TopazDecisionProvider depends on (LRN-038)
        // — resolved from the SAME singleton.
        services.Configure<TopazOptions>(configuration.GetSection(TopazOptions.SectionName));
        services.AddSingleton<TopazCheckService>();
        services.AddSingleton<ITopazCheckClient>(sp => sp.GetRequiredService<TopazCheckService>());
        services.AddSingleton<IAuthorizationDecisionProvider, Adapters.Topaz.TopazDecisionProvider>();

        services.AddSingleton<AuthorizationDecisionProviderFactory>();
        services.AddPdpDecisionAuditSink(configuration);
        services.AddSingleton<PdpDecisionService>();

        return services;
    }
}
