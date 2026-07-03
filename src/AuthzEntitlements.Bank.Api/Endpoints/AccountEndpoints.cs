using AuthzEntitlements.Bank.Api.Auth;
using AuthzEntitlements.Bank.Api.Contracts;
using AuthzEntitlements.Bank.Api.Data;
using AuthzEntitlements.Bank.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AuthzEntitlements.Bank.Api.Endpoints;

// CRUD for accounts (the primary authz resource). Create is minimal in CS02: it
// validates referential integrity and persists; richer rules land in later CSs.
// CS03: account creation is a privileged write, gated to the BranchManager role
// (interim rule — a dedicated account-lifecycle scope arrives with later authz CSs).
public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup("/api/accounts");

        accounts.MapGet("/", async (BankDbContext db, CancellationToken ct) =>
            TypedResults.Ok(
                await db.Accounts.AsNoTracking()
                    .OrderBy(a => a.AccountNumber)
                    .Select(a => a.ToDto())
                    .ToListAsync(ct)))
            .RequireAuthorization(AuthorizationSetup.ScopeReadPolicy);

        accounts.MapGet("/{id:guid}", async Task<IResult> (Guid id, BankDbContext db, CancellationToken ct) =>
        {
            var account = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
            return account is null ? TypedResults.NotFound() : TypedResults.Ok(account.ToDto());
        }).RequireAuthorization(AuthorizationSetup.ScopeReadPolicy);

        accounts.MapPost("/", async Task<IResult> (
            CreateAccountRequest request, BankDbContext db, CancellationToken ct) =>
        {
            if (!await db.Tenants.AnyAsync(t => t.Id == request.TenantId, ct))
            {
                return TypedResults.Problem($"Tenant {request.TenantId} does not exist.",
                    statusCode: StatusCodes.Status400BadRequest);
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
