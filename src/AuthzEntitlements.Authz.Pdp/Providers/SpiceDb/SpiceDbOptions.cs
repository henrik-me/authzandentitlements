namespace AuthzEntitlements.Authz.Pdp.Providers.SpiceDb;

// Config for the SpiceDB (ReBAC) adapter, bound from "Pdp:SpiceDb". Endpoint is empty by
// default so DI registration and the default `aspire run`/`dotnet test` never depend on a
// running server — the AppHost injects the real coordinates (Pdp__SpiceDb__Endpoint +
// Pdp__SpiceDb__PresharedKey) only when the spicedb container is started and Pdp:Provider is
// switched to "spicedb". SpiceDB is the head-to-head ReBAC counterpart to OpenFGA: it answers
// the SAME account-shaped relationship questions over a gRPC API instead of the OpenFGA SDK.
public sealed class SpiceDbOptions
{
    public const string SectionName = "Pdp:SpiceDb";

    // The SpiceDB gRPC endpoint (e.g. "http://localhost:50051"). A plain http:// address selects
    // the h2c (cleartext HTTP/2) transport the dev container serves; empty until the container
    // injects it, so the service fails closed with a clear message if a check is attempted while
    // blank. HTTPS endpoints would need TLS channel credentials (a documented follow-on).
    public string Endpoint { get; set; } = string.Empty;

    // The SpiceDB preshared key sent as an "Authorization: Bearer <key>" gRPC metadata header
    // (SpiceDB's `serve --grpc-preshared-key` auth). Empty by default; the AppHost injects the
    // dev container's key. A blank key still connects, but the server rejects the call and the
    // provider fails closed — never a silent permit.
    public string PresharedKey { get; set; } = string.Empty;
}
