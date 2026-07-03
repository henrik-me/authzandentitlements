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

// Marker: reached only when the coarse authorization policy passed (a denied
// request is short-circuited by UseAuthorization above). GatewayAuditMiddleware
// reads this to tell an edge deny apart from an edge allow that was routed to
// Bank.Api, so a downstream fine-grained 403 is audited as allow/routed.
app.Use(async (context, nextMiddleware) =>
{
    // Only a MATCHED endpoint (a YARP proxy route) carries the coarse policy that
    // UseAuthorization just evaluated and passed. Unmatched /api requests (no route
    // for the method) reach here with a null endpoint and no policy — they 404
    // without being routed, so they must NOT be recorded as an edge allow.
    if (context.GetEndpoint() is not null)
    {
        context.Items[GatewayAuditMiddleware.EdgeAuthorizedItemKey] = true;
    }
    await nextMiddleware(context);
});

app.MapReverseProxy();

app.Run();
