using System.Text.Json.Serialization;
using AuthzEntitlements.Audit.Service.Data;
using AuthzEntitlements.Audit.Service.Domain;
using AuthzEntitlements.Audit.Service.Endpoints;
using AuthzEntitlements.Audit.Service.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Aspire injects the "audit" connection string via .WithReference(auditDb) in the AppHost.
// We use the plain Npgsql EF Core provider so every EF package stays on the .NET 10 RC1 line
// that matches the installed runtime.
builder.Services.AddDbContext<AuditDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("audit")));

// CS36 (LRN-057, Decision #4): the authoritative request-snapshot size cap is configurable
// (default 16 KB). An over-cap snapshot degrades to null at ingest (fail-open, logged) rather than
// persisting an unbounded queryable blob.
builder.Services.Configure<RequestSnapshotOptions>(
    builder.Configuration.GetSection(RequestSnapshotOptions.SectionName));

// Serialize enums as their names so the wire contract is stable and readable.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// The chain writer serializes appends across the whole service instance, so it is a singleton
// and resolves a scoped AuditDbContext per append via IServiceScopeFactory.
builder.Services.AddSingleton<AuditChainWriter>();

var app = builder.Build();

// Apply migrations on startup. MigrateAsync is a no-op when up to date, so this is safe to run
// on every boot.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
    await db.Database.MigrateAsync();
}

app.MapDefaultEndpoints();

app.MapGet("/", () => TypedResults.Ok(new
{
    service = "AuthzEntitlements.Audit.Service",
    description =
        "Tamper-evident, append-only, hash-chained audit log for authz/entitlement decisions.",
    endpoints = new[]
    {
        "POST /api/audit/decisions",
        "GET /api/audit/verify",
        "GET /api/audit/entries",
    },
}));

app.MapAuditEndpoints();

app.Run();
