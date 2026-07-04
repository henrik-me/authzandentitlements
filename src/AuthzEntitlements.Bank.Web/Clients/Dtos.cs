using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuthzEntitlements.Bank.Web.Clients;

// Shared System.Text.Json settings for every typed client. Web defaults give
// camelCase property names + case-insensitive matching (mirroring the ASP.NET Core
// services these clients call), and the string enum converter matches the
// JsonStringEnumConverter every downstream service registers, so enum wire values are
// the member names ("Debit", "Active", "Permit") rather than brittle ordinals.
public static class BankJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

// ---- Bank.Api enums (mirror src/AuthzEntitlements.Bank.Api/Domain/Enums.cs) ----

public enum AccountType
{
    Checking,
    Savings,
    Loan,
}

public enum AccountStatus
{
    Active,
    Frozen,
    Closed,
}

public enum TransactionType
{
    Debit,
    Credit,
    Transfer,
}

public enum TransactionStatus
{
    Pending,
    Approved,
    Rejected,
    Posted,
}

public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected,
}

// ---- Bank.Api DTOs (mirror src/AuthzEntitlements.Bank.Api/Contracts/Dtos.cs) ----

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

public sealed record UserDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    string Username,
    string Email,
    string DisplayName,
    IReadOnlyList<string> Roles);

public sealed record CreateTransactionRequest(
    Guid AccountId,
    TransactionType Type,
    decimal Amount,
    Guid MakerId,
    string? Reference);

public sealed record DecideRequest(Guid CheckerId, string? Reason);

// ---- Entitlements.Service DTOs (mirror Entitlements.Service/Contracts/Dtos.cs) ----

public sealed record PlanSummaryResponse(
    string TenantCode,
    string PlanTier,
    int SeatLimit,
    int SeatsUsed,
    string[] Modules,
    string[] Features);

public sealed record FeatureEntitlementResponse(
    bool Enabled,
    string PlanTier,
    string Reason);

// ---- Governance.Service DTOs (mirror Governance.Service/Contracts/Dtos.cs) ----

public sealed record AccessPackageResponse(
    string Code,
    string DisplayName,
    string Description,
    int DefaultDurationMinutes,
    bool RequiresApproval,
    string[] Roles);

public sealed record AccessRequestResponse(
    Guid Id,
    string PrincipalId,
    string TenantCode,
    string AccessPackageCode,
    string Justification,
    int? RequestedDurationMinutes,
    string Status,
    string SodOutcome,
    string? SodReason,
    DateTimeOffset RequestedAt,
    string? DecidedBy,
    DateTimeOffset? DecidedAt);

public sealed record AccessGrantResponse(
    Guid Id,
    Guid RequestId,
    string PrincipalId,
    string TenantCode,
    string AccessPackageCode,
    string[] Roles,
    DateTimeOffset GrantedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    string? RevokedBy,
    bool Active,
    string Status);

public sealed record PrincipalAccessResponse(
    string PrincipalId,
    string TenantCode,
    string[] EffectiveRoles,
    string[] BaselineRoles,
    string[] ActiveGrantPackages);

public sealed record CreateAccessRequestBody(
    string PrincipalId,
    string AccessPackageCode,
    string Justification,
    int? RequestedDurationMinutes);

public sealed record ApproveRequestBody(string ApproverId);

public sealed record RejectRequestBody(string ApproverId, string? Reason);

// ---- Authz.Pdp native AuthZEN contract (mirror Authz.Pdp/Contracts/*.cs) ----

public sealed record PdpSubjectDto(
    string Type,
    string Id,
    IReadOnlyList<string> Roles,
    string? Tenant = null,
    string? Branch = null);

public sealed record PdpActionDto(string Name);

public sealed record PdpResourceDto(
    string Type,
    string? Id = null,
    string? Tenant = null,
    string? Branch = null,
    decimal? Amount = null,
    string? MakerId = null,
    string? Status = null);

public sealed record PdpContextDto(IReadOnlyList<string> Scopes);

public sealed record PdpAccessRequestDto(
    PdpSubjectDto Subject,
    PdpActionDto Action,
    PdpResourceDto Resource,
    PdpContextDto Context);

public sealed record PdpReasonDto(string Code, string Message);

public sealed record PdpObligationDto(string Id);

// Decision is the AuthZEN verdict string, "Permit" or "Deny".
public sealed record PdpDecisionDto(
    string Decision,
    IReadOnlyList<PdpReasonDto> Reasons,
    IReadOnlyList<PdpObligationDto>? Obligations);
