using AuthzEntitlements.Edge.Gateway.Audit;
using AuthzEntitlements.Edge.Gateway.Auth;
using AuthzEntitlements.Edge.Gateway.Telemetry;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Coarse edge gate: validate Keycloak-issued tokens (same authority/audience
// contract as Bank.Api) and register the four coarse policies. The AppHost
// injects the Keycloak authority/audience at runtime (see GatewayAuthenticationSetup).
builder.Services.AddGatewayJwtAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddCoarseAuthorization();
builder.Services.AddSingleton<GatewayMetrics>();

// YARP routes/clusters are declarative in configuration. The AppHost overrides the
// bank-api destination address at runtime via
// ReverseProxy__Clusters__bank-api__Destinations__bank-api__Address.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Surface the gateway's own ActivitySource + Meter to the OTel pipeline that
// ServiceDefaults configured, so coarse decisions appear in traces/metrics.
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(GatewayTelemetry.Name))
    .WithMetrics(metrics => metrics.AddMeter(GatewayTelemetry.Name));

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => TypedResults.Ok(new
{
    service = "AuthzEntitlements.Edge.Gateway",
    description = "Coarse-grained YARP edge in front of Bank.Api.",
    note = "/api/** is proxied to bank-api after coarse token/audience/scope/tenant checks; "
        + "fine-grained (role, resource-tenant, maker-checker) authorization stays in Bank.Api.",
}));

// Audit runs FIRST so it wraps auth/authz and observes the final status (incl. the
// 401/403 they short-circuit), then authentication → authorization → proxy.
app.UseMiddleware<GatewayAuditMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapReverseProxy();

app.Run();
