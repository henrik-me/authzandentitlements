using AuthzEntitlements.Bank.Api.Contracts;
using AuthzEntitlements.Bank.Api.Data;
using AuthzEntitlements.Bank.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AuthzEntitlements.Bank.Api.Endpoints;

// Transactions and their maker-checker approvals. Create applies the shared
// threshold rule (Transaction.Create); approve/reject enforce checker-role
// eligibility (endpoint layer) and segregation of duties (domain layer).
public static class TransactionEndpoints
{
    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var transactions = app.MapGroup("/api/transactions");

        transactions.MapGet("/", async (BankDbContext db, CancellationToken ct) =>
            TypedResults.Ok(
                await db.Transactions.AsNoTracking()
                    .Include(t => t.Approval)
                    .OrderBy(t => t.CreatedAt)
                    .Select(t => t.ToDto())
                    .ToListAsync(ct)));

        transactions.MapGet("/{id:guid}", async Task<IResult> (
            Guid id, BankDbContext db, CancellationToken ct) =>
        {
            var txn = await db.Transactions.AsNoTracking()
                .Include(t => t.Approval)
                .FirstOrDefaultAsync(t => t.Id == id, ct);
            return txn is null ? TypedResults.NotFound() : TypedResults.Ok(txn.ToDto());
        });

        transactions.MapPost("/", async Task<IResult> (
            CreateTransactionRequest request, BankDbContext db, CancellationToken ct) =>
        {
            if (request.Amount <= 0m)
            {
                return TypedResults.Problem("Transaction amount must be positive.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!await db.Accounts.AnyAsync(a => a.Id == request.AccountId
                    && a.TenantId == request.TenantId, ct))
            {
                return TypedResults.Problem(
                    $"Account {request.AccountId} does not exist in tenant {request.TenantId}.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (!await db.Users.AnyAsync(u => u.Id == request.MakerId
                    && u.TenantId == request.TenantId, ct))
            {
                return TypedResults.Problem(
                    $"Maker {request.MakerId} does not exist in tenant {request.TenantId}.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var (txn, approval) = Transaction.Create(
                request.TenantId, request.BranchId, request.AccountId, request.Type,
                request.Amount, request.Currency, request.MakerId, request.Reference,
                DateTimeOffset.UtcNow);

            db.Transactions.Add(txn);
            if (approval is not null)
            {
                db.Approvals.Add(approval);
            }

            await db.SaveChangesAsync(ct);
            return TypedResults.Created($"/api/transactions/{txn.Id}", txn.ToDto());
        });

        transactions.MapPost("/{id:guid}/approve", (
            Guid id, DecideRequest request, BankDbContext db, CancellationToken ct) =>
            DecideAsync(id, request, approve: true, db, ct));

        transactions.MapPost("/{id:guid}/reject", (
            Guid id, DecideRequest request, BankDbContext db, CancellationToken ct) =>
            DecideAsync(id, request, approve: false, db, ct));

        return app;
    }

    private static async Task<IResult> DecideAsync(
        Guid transactionId, DecideRequest request, bool approve, BankDbContext db, CancellationToken ct)
    {
        var txn = await db.Transactions
            .Include(t => t.Approval)
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct);

        if (txn is null)
        {
            return TypedResults.NotFound();
        }

        if (txn.Approval is null)
        {
            return TypedResults.Problem(
                $"Transaction {transactionId} has no approval to decide (below the approval threshold).",
                statusCode: StatusCodes.Status409Conflict);
        }

        var checker = await db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == request.CheckerId, ct);

        if (checker is null)
        {
            return TypedResults.Problem($"Checker {request.CheckerId} does not exist.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var isEligible = checker.UserRoles.Any(ur => ur.Role is not null
            && RoleNames.CheckerEligibleRoles.Contains(ur.Role.Name));
        if (!isEligible)
        {
            return TypedResults.Problem(
                $"Checker {request.CheckerId} is not authorized to decide approvals " +
                $"(requires {string.Join(" or ", RoleNames.CheckerEligibleRoles)}).",
                statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            txn.Approval.Decide(request.CheckerId, approve, request.Reason, DateTimeOffset.UtcNow);
        }
        catch (SegregationOfDutiesViolationException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(txn.ToDto());
    }
}
