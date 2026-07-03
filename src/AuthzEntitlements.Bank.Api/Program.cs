using System.Text.Json.Serialization;
using AuthzEntitlements.Bank.Api.Auth;
using AuthzEntitlements.Bank.Api.Data;
using AuthzEntitlements.Bank.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Validate Keycloak-issued access tokens (AuthN) and register the authorization
// policy contract (AuthZ). The Keycloak authority/audience are injected at runtime
// by the AppHost via configuration (see AuthenticationSetup for the exact keys).
builder.Services.AddBankJwtAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddBankAuthorization();


// Aspire injects the "bank" connection string via .WithReference(bankDb) in the
// AppHost. We use the plain Npgsql EF Core provider so every EF package stays on
// the .NET 10 RC1 line that matches the installed runtime.
builder.Services.AddDbContext<BankDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("bank")));

// Serialize enums as their names so the wire contract is stable and readable.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// Apply migrations and seed on startup. MigrateAsync is a no-op when up to date and
// the seeder guards on existing data, so this is safe to run on every boot.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BankDbContext>();
    await db.Database.MigrateAsync();
    await BankSeeder.SeedAsync(db);
}

app.MapDefaultEndpoints();

// Audit middleware wraps auth so it observes the FINAL status, including the
// 401/403 that authentication/authorization short-circuit. It emits one
// structured, audit-ready fine-grained authorization-decision event per /api
// request (CS13 ingests these).
app.UseMiddleware<BankAuthorizationAuditMiddleware>();

// AuthN/AuthZ middleware. Endpoints without RequireAuthorization (the "/" info
// endpoint and health checks) remain anonymous.
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => TypedResults.Ok(new
{
    service = "AuthzEntitlements.Bank.Api",
    description = "Fintech back-office domain API (accounts, transactions, approvals).",
    endpoints = new[]
    {
        "/api/tenants", "/api/branches", "/api/users", "/api/accounts", "/api/transactions",
    },
}));

app.MapReferenceEndpoints();
app.MapAccountEndpoints();
app.MapTransactionEndpoints();

app.Run();
