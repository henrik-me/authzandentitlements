using AuthzEntitlements.Bank.Api.Contracts;
using AuthzEntitlements.Bank.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AuthzEntitlements.Bank.Api.Endpoints;

// Read-only reference data: tenants, branches, and users. These back the subject
// and resource attributes later authz layers evaluate.
public static class ReferenceEndpoints
{
    public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        var tenants = app.MapGroup("/api/tenants");

        tenants.MapGet("/", async (BankDbContext db, CancellationToken ct) =>
            TypedResults.Ok(
                await db.Tenants.AsNoTracking()
                    .OrderBy(t => t.Code)
                    .Select(t => t.ToDto())
                    .ToListAsync(ct)));

        tenants.MapGet("/{id:guid}", async Task<IResult> (Guid id, BankDbContext db, CancellationToken ct) =>
        {
            var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
            return tenant is null ? TypedResults.NotFound() : TypedResults.Ok(tenant.ToDto());
        });

        app.MapGroup("/api/branches").MapGet("/", async (BankDbContext db, CancellationToken ct) =>
            TypedResults.Ok(
                await db.Branches.AsNoTracking()
                    .OrderBy(b => b.Code)
                    .Select(b => b.ToDto())
                    .ToListAsync(ct)));

        app.MapGroup("/api/users").MapGet("/", async (BankDbContext db, CancellationToken ct) =>
            TypedResults.Ok(
                (await db.Users.AsNoTracking()
                    .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                    .OrderBy(u => u.TenantId).ThenBy(u => u.Username)
                    .ToListAsync(ct))
                    .Select(u => u.ToDto())
                    .ToList()));

        return app;
    }
}
