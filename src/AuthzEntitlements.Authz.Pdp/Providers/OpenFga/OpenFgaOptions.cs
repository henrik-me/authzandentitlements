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
}
