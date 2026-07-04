namespace AuthzEntitlements.Authz.Pdp.Providers.OpenFga;

// Config for the OpenFGA (ReBAC) adapter, bound from "Pdp:OpenFga". ApiUrl is empty by
// default so DI registration and the default `aspire run`/`dotnet test` never depend on a
// running server — the AppHost injects the real endpoint (Pdp__OpenFga__ApiUrl) only when the
// openfga container is started and Pdp:Provider is switched to "openfga". The store is created
// or reused by StoreName on first use.
public sealed class OpenFgaOptions
{
    public const string SectionName = "Pdp:OpenFga";

    // The OpenFGA HTTP API base URL (e.g. "http://localhost:8080"). Empty until the container
    // injects it; the service fails closed with a clear message if a check is attempted while blank.
    public string ApiUrl { get; set; } = string.Empty;

    // The OpenFGA store the model + tuples live in. Created on first bootstrap if absent, reused by
    // name across restarts.
    public string StoreName { get; set; } = "authz-rebac";

    // An OpenFGA authorization-model id to PIN (e.g. "01JABC..."). When set, bootstrap reuses that
    // existing model version instead of writing the embedded model every boot — avoiding per-boot
    // model-version growth on a persistent shared store (LRN-031). Empty (the default) preserves the
    // write-then-pin behaviour: a fresh store gets the embedded CS07 model written and the returned id
    // pinned for the process. A blank/whitespace value is treated as unset, so a misconfigured empty
    // string never pins a bogus id — it falls back to write-then-pin (fail-safe).
    public string AuthorizationModelId { get; set; } = string.Empty;
}
