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

// ---- AuthZ Playground fan-out contract (mirror Authz.Pdp/Playground/PlaygroundModels.cs) ----
// POST /api/authz/playground/fanout — run ONE AccessRequest across every registered engine
// (or a named subset) and return per-engine comparable results, so the playground UI can render
// a side-by-side decision/explanation/latency comparison. camelCase JSON, JsonStringEnumConverter.

// One fan-out request: the AccessRequest to evaluate plus an optional engine subset. A null or
// empty Engines list fans out across every registered provider.
public sealed record PlaygroundFanoutRequestDto(
    PdpAccessRequestDto Request,
    IReadOnlyList<string>? Engines = null);

// One engine-native artifact that contributed to a decision (mirror Contracts/PolicyReference).
public sealed record PdpPolicyReferenceDto(string Kind, string Reference, string? Detail = null);

// The engine-agnostic explanation attached to a decision (mirror Contracts/DecisionExplanation):
// a normalized DeterminingRule plus each engine's native determining artifact(s).
public sealed record PdpExplanationDto(
    string Engine,
    string DeterminingRule,
    IReadOnlyList<PdpPolicyReferenceDto> PolicyReferences,
    string Narrative);

// One engine's answer to the fanned-out request. Available is the reachability verdict; when
// false, UnavailableReason carries the failure message and the row is excluded from AllAgree.
public sealed record EngineDecisionResultDto(
    string Engine,
    string Decision,
    IReadOnlyList<PdpReasonDto> Reasons,
    IReadOnlyList<PdpObligationDto>? Obligations,
    PdpExplanationDto? Explanation,
    double LatencyMs,
    string? TraceId,
    bool Available,
    string? UnavailableReason);

// The whole fan-out: every engine's result, the best-effort top-level trace id, and AllAgree —
// computed server-side over only the AVAILABLE engines.
public sealed record PlaygroundFanoutResponseDto(
    IReadOnlyList<EngineDecisionResultDto> Results,
    string? TraceId,
    bool AllAgree);

// ---- Audit.Service read model (mirror Audit.Service/Contracts/AuditResponses.cs) ----

// One tamper-evident audit-log row (mirror AuditEntryView). PrevHash/RowHash link the chain.
public sealed record AuditEntryDto(
    long Sequence,
    DateTimeOffset TimestampUtc,
    string TraceId,
    string Provider,
    string SubjectId,
    string Action,
    string ResourceType,
    string? ResourceId,
    string Decision,
    string Reason,
    string? Tenant,
    string Producer,
    string PrevHash,
    string RowHash);

// The result of recomputing the whole hash chain (mirror ChainVerificationResponse): Valid plus,
// when broken, the sequence and reason the recomputation first diverged.
public sealed record ChainVerificationDto(
    bool Valid,
    long EntryCount,
    long? BrokenAtSequence,
    string? Reason,
    long? TailSequence,
    string? TailRowHash);

// Convenience input carrying the /api/audit/entries filter fields. Non-null fields become
// query-string parameters (AND semantics); nulls are omitted.
public sealed record AuditQuery(
    long? Sequence = null,
    string? Subject = null,
    string? Action = null,
    string? Decision = null,
    string? Tenant = null,
    string? Trace = null,
    string? Producer = null,
    int? Limit = null,
    int? Offset = null);
