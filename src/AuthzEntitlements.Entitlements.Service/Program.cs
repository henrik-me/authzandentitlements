using System.Text.Json.Serialization;
using AuthzEntitlements.Entitlements.Service.Data;
using AuthzEntitlements.Entitlements.Service.Endpoints;
using AuthzEntitlements.Entitlements.Service.Features;
using AuthzEntitlements.Entitlements.Service.Metering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenFeature;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Additively register the entitlements Meter on the OTel metrics pipeline that
// AddServiceDefaults configured, so decision/quota counters are exported alongside the
// default runtime metrics.
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics.AddMeter(EntitlementsMetrics.MeterName));

// Aspire injects the "entitlements" connection string via .WithReference(entitlementsDb)
// in the AppHost. We use the plain Npgsql EF Core provider so every EF package stays on
// the .NET 10 RC1 line that matches the installed runtime.
builder.Services.AddDbContext<EntitlementsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("entitlements")));

// Serialize enums as their names so the wire contract is stable and readable.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.Configure<EntitlementsFeatureOptions>(
    builder.Configuration.GetSection(EntitlementsFeatureOptions.SectionName));

builder.Services.AddSingleton<EntitlementsMetrics>();
builder.Services.AddSingleton<IEntitlementAuditSink, LoggingEntitlementAuditSink>();
builder.Services.AddSingleton<IFeatureGate, OpenFeatureGate>();

var app = builder.Build();

// Apply migrations and seed on startup. MigrateAsync is a no-op when up to date and the
// seeder guards on existing data, so this is safe to run on every boot. Setting the
// OpenFeature provider here (not during service configuration) keeps provider
// construction off the design-time path used by `dotnet ef migrations`.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EntitlementsDbContext>();
    await db.Database.MigrateAsync();
    await EntitlementsSeeder.SeedAsync(db);
}

var featureOptions = app.Services.GetRequiredService<IOptions<EntitlementsFeatureOptions>>().Value;
await Api.Instance.SetProviderAsync(FeatureProviderFactory.Create(featureOptions));

app.MapDefaultEndpoints();

app.MapGet("/", () => TypedResults.Ok(new
{
    service = "AuthzEntitlements.Entitlements.Service",
    description = "Commercial entitlements: plans, modules, seats, feature gates, and usage quotas.",
    endpoints = new[]
    {
        "/api/entitlements/{tenantCode}/plan",
        "/api/entitlements/{tenantCode}/modules/{moduleKey}",
        "/api/entitlements/{tenantCode}/features/{featureKey}",
        "/api/entitlements/{tenantCode}/quotas/{quotaKey}/consume",
        "/api/entitlements/{tenantCode}/seats",
    },
}));

app.MapEntitlementsEndpoints();

app.Run();
