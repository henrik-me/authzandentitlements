using System.Security.Claims;
using AuthzEntitlements.Bank.Api.Auth;
using AuthzEntitlements.Bank.Api.Contracts;
using AuthzEntitlements.Bank.Api.Data;
using AuthzEntitlements.Bank.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AuthzEntitlements.Bank.Api.Endpoints;

// CRUD for accounts (the primary authz resource). Reads are tenant-scoped to the
// caller's token tenant; create is gated to the BranchManager role AND the caller's
// tenant. Auth is an outer gate; the domain layer still owns its invariants.
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup("/api/accounts");

        accounts.MapGet("/", async Task<IResult> (
            ClaimsPrincipal user, BankDbContext db, CancellationToken ct) =>
        {
            var tenantId = await user.ResolveCallerTenantIdAsync(db, ct);
            if (tenantId is null)
            {
                return TypedResults.Forbid();
            }

            return TypedResults.Ok(
                await db.Accounts.AsNoTracking()
                    .Where(a => a.TenantId == tenantId)
                    .OrderBy(a => a.AccountNumber)
                    .Select(a => a.ToDto())
                    .ToListAsync(ct));
        }).RequireAuthorization(AuthorizationSetup.ScopeReadPolicy);

        accounts.MapGet("/{id:guid}", async Task<IResult> (
            Guid id, ClaimsPrincipal user, BankDbContext db, CancellationToken ct) =>
        {
            var tenantId = await user.ResolveCallerTenantIdAsync(db, ct);
            if (tenantId is null)
            {
                return TypedResults.Forbid();
            }

            var account = await db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId, ct);
            return account is null ? TypedResults.NotFound() : TypedResults.Ok(account.ToDto());
        }).RequireAuthorization(AuthorizationSetup.ScopeReadPolicy);

        accounts.MapPost("/", async Task<IResult> (
            CreateAccountRequest request, ClaimsPrincipal user, BankDbContext db, CancellationToken ct) =>
        {
            // The caller may only create accounts within their own token tenant.
            var callerTenantId = await user.ResolveCallerTenantIdAsync(db, ct);
            if (callerTenantId is null || callerTenantId != request.TenantId)
            {
                return TypedResults.Forbid();
            }

            if (!await db.Branches.AnyAsync(b => b.Id == request.BranchId
                    && b.TenantId == request.TenantId, ct))
            {
                return TypedResults.Problem(
                    $"Branch {request.BranchId} does not exist in tenant {request.TenantId}.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var account = new Account
            {
                Id = Guid.NewGuid(),
                TenantId = request.TenantId,
                BranchId = request.BranchId,
                AccountNumber = request.AccountNumber,
                CustomerName = request.CustomerName,
                Type = request.Type,
                Balance = request.Balance,
                Currency = request.Currency,
                Status = AccountStatus.Active,
            };

            db.Accounts.Add(account);
            await db.SaveChangesAsync(ct);
            return TypedResults.Created($"/api/accounts/{account.Id}", account.ToDto());
        }).RequireAuthorization(RoleNames.BranchManager);

        return app;
    }
}
