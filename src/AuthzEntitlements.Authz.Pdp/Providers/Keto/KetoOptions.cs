namespace AuthzEntitlements.Authz.Pdp.Providers.Keto;

// Config for the Ory Keto (ReBAC) adapter, bound from "Pdp:Keto". Both endpoints are empty by
// default so DI registration and the default `aspire run`/`dotnet test` never depend on a running
// server — the AppHost injects the real coordinates (Pdp__Keto__ReadEndpoint +
// Pdp__Keto__WriteEndpoint) only when the keto container is started and Pdp:Provider is switched to
// "keto". Keto is the head-to-head ReBAC counterpart to SpiceDB and OpenFGA: it answers the SAME
// account-shaped relationship questions over an HTTP REST API instead of SpiceDB's gRPC / OpenFGA's
// SDK.
//
// Keto deliberately splits its API across TWO ports: a READ port (default 4466) that answers
// permission checks, and a WRITE port (default 4467) that mutates relationships. The adapter checks
// permissions against ReadEndpoint and seeds the shared relationship graph against WriteEndpoint, so
// both must be configured before a live check can run.
public sealed class KetoOptions
{
    public const string SectionName = "Pdp:Keto";

    // The Keto READ API base address (e.g. "http://localhost:4466"), where permission checks are
    // issued. A plain http:// address is the dev container's cleartext REST endpoint; https:// is
    // equally valid (Keto is HTTP REST, not gRPC/h2c, so no transport switch is needed). Empty until
    // the container injects it, so the service fails closed with a clear message if a check is
    // attempted while blank.
    public string ReadEndpoint { get; set; } = string.Empty;

    // The Keto WRITE API base address (e.g. "http://localhost:4467"), where the shared seed
    // relationships are created (an appending PUT to /admin/relation-tuples — NOT an idempotent upsert;
    // harmless here because the in-memory dev store is seeded once per process). Empty by default; the
    // AppHost injects the dev container's write port. A blank write endpoint fails the bootstrap closed
    // rather than silently seeding nothing — never a silent permit.
    public string WriteEndpoint { get; set; } = string.Empty;
}
