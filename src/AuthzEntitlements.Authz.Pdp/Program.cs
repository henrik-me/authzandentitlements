using System.Text.Json.Serialization;
using AuthzEntitlements.Authz.Pdp.Endpoints;
using AuthzEntitlements.Authz.Pdp.Lifecycle;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Services;
using AuthzEntitlements.Authz.Pdp.Telemetry;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Surface the PDP's own ActivitySource + Meter on the OTel pipeline that AddServiceDefaults
// configured, so every decision appears in traces/metrics. CS05 wires the hooks only; CS12
// stands up the collector/dashboards that consume them.
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(PdpTelemetry.Name))
    .WithMetrics(metrics => metrics.AddMeter(PdpTelemetry.Name));

// Serialize enums as their names so the decision wire contract is stable and readable.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Bind PdpOptions and register providers, the name-based selection factory, the audit
// sink, and the decision service that fires the audit/OTel hooks around the selected
// provider. This is the seam CS06-CS09 add their engine adapters to.
builder.Services.AddPdp(builder.Configuration);

// CS17 — policy-lifecycle tooling (shadow / dual-run comparison + what-if simulation) layered
// over the provider factory. No external dependency; the deterministic default run is unchanged.
builder.Services.AddPolicyLifecycle();

var app = builder.Build();

// Force-resolve the decision service at startup so a misconfigured "Pdp:Provider" fails
// fast (fail closed) rather than surfacing only on the first request.
_ = app.Services.GetRequiredService<PdpDecisionService>();

app.MapDefaultEndpoints();

app.MapGet("/", () => TypedResults.Ok(new
{
    service = "AuthzEntitlements.Authz.Pdp",
    description = "Unified AuthZEN-aligned fine-grained PDP: one decision contract, pluggable engines.",
    endpoints = new[]
    {
        "/api/authz/evaluate",
        "/api/authz/scenarios",
        "/api/authz/scenarios/verify",
        "/api/authz/whatif",
        "/api/authz/shadow",
        "/api/authz/shadow/catalog",
        "/api/authz/policy/version",
        "/api/authz/authzen/evaluation",
        "/api/authz/rebac/verify",
        "/api/authz/rebac/who-can-access",
        "/api/authz/rebac/what-can-user-access",
    },
}));

app.MapPdpEndpoints();
app.MapRebacEndpoints();
app.MapPolicyLifecycleEndpoints();
app.MapAuthZenEndpoints();

app.Run();
