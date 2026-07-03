using AuthzEntitlements.Bank.Api.Domain;

namespace AuthzEntitlements.Bank.Api.Contracts;

// DTOs returned to callers. Endpoints project entities into these so navigation
// cycles never reach the serializer and the wire contract stays decoupled from the
// EF model.

public sealed record TenantDto(Guid Id, string Name, string Code);

public sealed record BranchDto(Guid Id, Guid TenantId, Guid RegionId, string Name, string Code);

public sealed record UserDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    string Username,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles);

public sealed record AccountDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    string AccountNumber,
    string CustomerName,
    AccountType Type,
    decimal Balance,
    string Currency,
    AccountStatus Status);

public sealed record ApprovalDto(
    Guid Id,
    Guid TransactionId,
    Guid MakerId,
    Guid? CheckerId,
    ApprovalStatus Status,
    string? DecisionReason,
    DateTimeOffset RequestedAt,
    DateTimeOffset? DecidedAt);

public sealed record TransactionDto(
    Guid Id,
    Guid AccountId,
    Guid TenantId,
    Guid BranchId,
    TransactionType Type,
    decimal Amount,
    string Currency,
    TransactionStatus Status,
    Guid MakerId,
    DateTimeOffset CreatedAt,
    string? Reference,
    ApprovalDto? Approval);

public sealed record CreateAccountRequest(
    Guid TenantId,
    Guid BranchId,
    string AccountNumber,
    string CustomerName,
    AccountType Type,
    decimal Balance,
    string Currency);

public sealed record CreateTransactionRequest(
    Guid TenantId,
    Guid BranchId,
    Guid AccountId,
    TransactionType Type,
    decimal Amount,
    string Currency,
    Guid MakerId,
    string? Reference);

public sealed record DecideRequest(Guid CheckerId, string? Reason);

public static class DtoMappings
{
    public static TenantDto ToDto(this Tenant t) => new(t.Id, t.Name, t.Code);

    public static BranchDto ToDto(this Branch b) =>
        new(b.Id, b.TenantId, b.RegionId, b.Name, b.Code);

    public static AccountDto ToDto(this Account a) =>
        new(a.Id, a.TenantId, a.BranchId, a.AccountNumber, a.CustomerName, a.Type,
            a.Balance, a.Currency, a.Status);

    public static ApprovalDto ToDto(this Approval a) =>
        new(a.Id, a.TransactionId, a.MakerId, a.CheckerId, a.Status, a.DecisionReason,
            a.RequestedAt, a.DecidedAt);

    public static TransactionDto ToDto(this Transaction t) =>
        new(t.Id, t.AccountId, t.TenantId, t.BranchId, t.Type, t.Amount, t.Currency,
            t.Status, t.MakerId, t.CreatedAt, t.Reference, t.Approval?.ToDto());

    public static UserDto ToDto(this User u) =>
        new(u.Id, u.TenantId, u.BranchId, u.Username, u.Email, u.DisplayName,
            u.UserRoles
                .Where(ur => ur.Role is not null)
                .Select(ur => ur.Role.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList());
}
