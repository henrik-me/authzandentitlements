using System.Text.Json.Serialization;
using AuthzEntitlements.Governance.Service.Auth;
using AuthzEntitlements.Governance.Service.BreakGlass;
using AuthzEntitlements.Governance.Service.Data;
using AuthzEntitlements.Governance.Service.Delegation;
using AuthzEntitlements.Governance.Service.Endpoints;
using AuthzEntitlements.Governance.Service.Metering;
using AuthzEntitlements.Governance.Service.Sod;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Additively register the governance Meter on the OTel metrics pipeline that
// AddServiceDefaults configured, so decision/grant/review counters are exported alongside
// the default runtime metrics.
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(GovernanceMetrics.MeterName));

// Aspire injects the "governance" connection string via .WithReference(governanceDb) in
// the AppHost. We use the plain Npgsql EF Core provider so every EF package stays on the
// .NET 10 RC1 line that matches the installed runtime.
builder.Services.AddDbContext<GovernanceDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("governance")));

// Serialize enums as their names so the wire contract is stable and readable.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton<GovernanceMetrics>();
builder.Services.AddSingleton<IGovernanceAuditSink, LoggingGovernanceAuditSink>();

// CS21 — break-glass and delegation grants are in-memory, time-boxed stores (no EF migration,
// no Postgres) so they must be process-wide singletons to be the system-of-record across
// requests. They mirror the AccessGrant.IsActive(now) read-time-expiry pattern.
builder.Services.AddSingleton<BreakGlassGrantStore>();
builder.Services.AddSingleton<DelegationGrantStore>();

// CS29 — JWT bearer authentication for the tenant-scoped access-request endpoints. Those
// endpoints bind their tenant boundary to the caller's validated token (never a caller-
// supplied field), so they require an authenticated Keycloak access token; the AppHost
// injects the Keycloak authority + audience (see GovernanceAuthenticationSetup for the
// config-key contract). Every OTHER governance endpoint stays anonymous so the intra-cluster
// read paths and the Compliance service keep working (see GovernanceEndpoints).
builder.Services.AddGovernanceJwtAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddAuthorization();

// Segregation-of-duties checks go through the PDP over HTTP (per the CS11 plan review,
// SoD is evaluated by the PDP's OpaProvider, not by coupling directly to OPA). The
// "https+http://authz-pdp" scheme is resolved by Aspire service discovery and
// AddServiceDefaults already wraps every HttpClient in the standard resilience handler.
builder.Services.AddHttpClient<IPdpSodClient, PdpSodClient>(client =>
    client.BaseAddress = new Uri("https+http://authz-pdp"));

// The approval decision service is stateless; it takes a fresh PDP client per request.
builder.Services.AddScoped<AccessApprovalService>();

var app = builder.Build();

// Apply migrations and seed on startup. MigrateAsync is a no-op when up to date and the
// seeder guards on existing data, so this is safe to run on every boot.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GovernanceDbContext>();
    await db.Database.MigrateAsync();
    await GovernanceSeeder.SeedAsync(db);
}

// CS29 — AuthN/AuthZ middleware. Endpoints without RequireAuthorization (access packages,
// principals, grants, review campaigns, the "/" info endpoint, and health checks) remain
// anonymous; only the access-request endpoints are gated (see GovernanceEndpoints).
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();

app.MapGet("/", () => TypedResults.Ok(new
{
    service = "AuthzEntitlements.Governance.Service",
    description = "Access governance: access packages, JIT elevation with maker-checker + SoD " +
                  "approval, time-bound grants, and access-review campaigns.",
    endpoints = new[]
    {
        "/api/governance/access-packages",
        "/api/governance/requests",
        "/api/governance/requests/{id}/approve",
        "/api/governance/principals/{id}/access",
        "/api/governance/grants/{id}/revoke",
        "/api/governance/review-campaigns",
        "/api/governance/break-glass",
        "/api/governance/break-glass/pending-review",
        "/api/governance/break-glass/{id}/review",
        "/api/governance/delegations",
        "/api/governance/delegations/{id}/revoke",
    },
}));

app.MapGovernanceEndpoints();
app.MapBreakGlassDelegationEndpoints();

app.Run();
