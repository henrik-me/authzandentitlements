using System.Security.Claims;
using AuthzEntitlements.Bank.Api.Auth;
using AuthzEntitlements.Bank.Api.Contracts;
using AuthzEntitlements.Bank.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AuthzEntitlements.Bank.Api.Endpoints;

// Read-only reference data: tenants, branches, and users. Every response is
// tenant-scoped to the caller's token tenant (fail closed when the claim is
// absent/unknown), so a caller never sees another tenant's subjects or resources.
public static class ReferenceEndpoints
{
    public static IEndpointRouteBuilder MapReferenceEndpoints(this IEndpointRouteBuilder app)
    {
        var tenants = app.MapGroup("/api/tenants").RequireAuthorization(AuthorizationSetup.ScopeReadPolicy);

        tenants.MapGet("/", async Task<IResult> (
            ClaimsPrincipal user, BankDbContext db, CancellationToken ct) =>
        {
            var tenantId = await user.ResolveCallerTenantIdAsync(db, ct);
            if (tenantId is null)
            {
                return TypedResults.Forbid();
            }

            return TypedResults.Ok(
                await db.Tenants.AsNoTracking()
                    .Where(t => t.Id == tenantId)
                    .OrderBy(t => t.Code)
                    .Select(t => t.ToDto())
                    .ToListAsync(ct));
        });

        tenants.MapGet("/{id:guid}", async Task<IResult> (
            Guid id, ClaimsPrincipal user, BankDbContext db, CancellationToken ct) =>
        {
            var tenantId = await user.ResolveCallerTenantIdAsync(db, ct);
            if (tenantId is null)
            {
                return TypedResults.Forbid();
            }

            var tenant = await db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id && t.Id == tenantId, ct);
            return tenant is null ? TypedResults.NotFound() : TypedResults.Ok(tenant.ToDto());
        });

        app.MapGroup("/api/branches").RequireAuthorization(AuthorizationSetup.ScopeReadPolicy)
            .MapGet("/", async Task<IResult> (
                ClaimsPrincipal user, BankDbContext db, CancellationToken ct) =>
            {
                var tenantId = await user.ResolveCallerTenantIdAsync(db, ct);
                if (tenantId is null)
                {
                    return TypedResults.Forbid();
                }

                return TypedResults.Ok(
                    await db.Branches.AsNoTracking()
                        .Where(b => b.TenantId == tenantId)
                        .OrderBy(b => b.Code)
                        .Select(b => b.ToDto())
                        .ToListAsync(ct));
            });

        app.MapGroup("/api/users").RequireAuthorization(AuthorizationSetup.ScopeReadPolicy)
            .MapGet("/", async Task<IResult> (
                ClaimsPrincipal user, BankDbContext db, CancellationToken ct) =>
            {
                var tenantId = await user.ResolveCallerTenantIdAsync(db, ct);
                if (tenantId is null)
                {
                    return TypedResults.Forbid();
                }

                return TypedResults.Ok(
                    (await db.Users.AsNoTracking()
                        .Where(u => u.TenantId == tenantId)
                        .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
                        .OrderBy(u => u.Username)
                        .ToListAsync(ct))
                        .Select(u => u.ToDto())
                        .ToList());
            });

        return app;
    }
}
