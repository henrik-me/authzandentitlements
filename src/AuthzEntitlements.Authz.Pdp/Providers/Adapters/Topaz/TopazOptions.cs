namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters.Topaz;

// Config for the out-of-process Topaz (Aserto) full-decision PDP adapter, bound from "Pdp:Topaz".
// Topaz is OPA-based: it runs an OPA policy bundle behind an Aserto authorizer API. This adapter drives
// it as a FULL-DECISION engine over the SAME Rego the OPA adapter uses (infra/opa/policy), so Topaz
// answers the shared fintech question identically to the reference/OPA engines. It is the head-to-head
// "OPA standalone vs OPA-inside-Topaz". Topaz's Zanzibar directory/ReBAC path is deliberately NOT used
// for the decision (documented parity boundary — see docs/authz/topaz-adapter.md).
//
// Endpoint is empty by default so DI registration and the default `aspire run`/`dotnet test` never
// depend on a running server — the AppHost injects the real coordinates (Pdp__Topaz__Endpoint) only
// when the topaz container is started and Pdp:Provider is switched to "topaz". Like CerbosOptions, the
// blank endpoint makes the service fail closed with a clear message if a check is attempted while
// unconfigured, rather than reaching for a default localhost server.
public sealed class TopazOptions
{
    public const string SectionName = "Pdp:Topaz";

    // The Topaz authorizer gRPC endpoint (e.g. "https://localhost:8282"). Topaz serves the authorizer
    // over TLS with a self-signed dev certificate, so an https:// address is expected and the adapter
    // connects in "insecure" mode (accepts the self-signed cert — a lab posture, never a deployment).
    // A plain http:// address selects the cleartext h2c transport instead. Empty until the container
    // injects it, so the service fails closed with a clear message if a check is attempted while blank.
    public string Endpoint { get; set; } = string.Empty;

    // Optional Aserto authorizer API key ("Authorization: basic <key>"). Empty by default: the lab
    // Topaz config (infra/topaz/config.yaml) enables anonymous access and disables the API key, so no
    // key is needed. Populated only if a key-protected authorizer is targeted.
    public string ApiKey { get; set; } = string.Empty;

    // Optional Aserto tenant id ("Aserto-Tenant-Id" header). Empty by default (single local instance);
    // populated only when targeting a multi-tenant Aserto authorizer.
    public string TenantId { get; set; } = string.Empty;

    // Bounded per-query fail-closed timeout in seconds (mirrors OpaOptions.TimeoutSeconds). A hung or
    // unreachable authorizer must fail closed PROMPTLY rather than block the evaluation indefinitely: the
    // forward Query is bounded to this deadline and a timeout surfaces as a Deny/ProviderUnavailable,
    // never a silent permit. Must be greater than zero — a non-positive value fails closed with a clear
    // message on first use.
    public int TimeoutSeconds { get; set; } = 5;
}
