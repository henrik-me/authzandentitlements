using System.Text.Json.Serialization;
using AuthzEntitlements.Governance.Service.Data;
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
    },
}));

app.MapGovernanceEndpoints();

app.Run();
