namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters.Cerbos;

// Config for the out-of-process Cerbos (full-decision PDP) adapter, bound from "Pdp:Cerbos".
// Endpoint is empty by default so DI registration and the default `aspire run`/`dotnet test`
// never depend on a running server — the AppHost injects the real coordinates
// (Pdp__Cerbos__Endpoint) only when the cerbos container is started and Pdp:Provider is switched
// to "cerbos". Cerbos is the head-to-head, out-of-process full-decision counterpart to OPA: it
// answers the SAME fintech question, natively over gRPC (YAML policies) instead of OPA's REST/Rego.
public sealed class CerbosOptions
{
    public const string SectionName = "Pdp:Cerbos";

    // The Cerbos gRPC endpoint (e.g. "http://localhost:3593"). A plain http:// address selects the
    // h2c (cleartext HTTP/2) transport the dev container serves; empty until the container injects
    // it, so the service fails closed with a clear message if a check is attempted while blank.
    // HTTPS endpoints would need TLS channel credentials (a documented follow-on).
    public string Endpoint { get; set; } = string.Empty;
}
