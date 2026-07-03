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

            // Load the full account so tenant/branch/currency are derived from a
            // trusted source rather than trusting caller-supplied values.
            var account = await db.Accounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == request.AccountId, ct);
            if (account is null)
            {
                return TypedResults.Problem(
                    $"Account {request.AccountId} does not exist.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var maker = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == request.MakerId, ct);
            if (maker is null)
            {
                return TypedResults.Problem(
                    $"Maker {request.MakerId} does not exist.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (maker.TenantId != account.TenantId)
            {
                return TypedResults.Problem(
                    $"Maker {request.MakerId} belongs to a different tenant than account {request.AccountId}.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var (txn, approval) = Transaction.Create(
                account.TenantId, account.BranchId, account.Id, request.Type,
                request.Amount, account.Currency, request.MakerId, request.Reference,
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

        // The transaction carries a trustworthy TenantId (derived from the account at
        // creation). A checker may only decide within their own tenant. Branch-level
        // scoping is deferred to later ABAC work.
        if (checker.TenantId != txn.TenantId)
        {
            return TypedResults.Problem(
                $"Checker {request.CheckerId} may not decide approvals in another tenant.",
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

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another approve/reject won the race and decided this approval first.
            // The xmin token made our UPDATE match zero rows; surface a 409 so the
            // caller reloads and retries instead of silently double-deciding.
            return TypedResults.Problem(
                "The approval was concurrently decided; reload and retry.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Ok(txn.ToDto());
    }
}
