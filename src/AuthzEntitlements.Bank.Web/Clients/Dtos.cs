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

// ---- Governance.Service CS21 break-glass + delegation DTOs (mirror Governance.Service/Contracts/Dtos.cs) ----

// Body for POST /api/governance/break-glass. Action is the bank action class the emergency grant
// covers (e.g. "bank.account.read"); DurationMinutes must be positive.
public sealed record IssueBreakGlassRequest(
    string PrincipalId,
    string TenantCode,
    string Action,
    string Justification,
    int DurationMinutes);

// Body for POST /api/governance/break-glass/{id}/review — records the mandatory post-review.
// Outcome is a free-form disposition label (e.g. "approved" / "rejected").
public sealed record ReviewBreakGlassRequest(string ReviewedBy, string Outcome);

// Body for POST /api/governance/delegations. Scopes are the delegated agent.bank.* capability
// scopes the delegate may exercise on the manager's behalf; DurationMinutes must be positive.
public sealed record CreateDelegationRequest(
    string ManagerId,
    string DelegateId,
    string TenantCode,
    IReadOnlyList<string> Scopes,
    int DurationMinutes);

// Body for POST /api/governance/delegations/{id}/revoke.
public sealed record RevokeDelegationRequest(string RevokedBy);

// A break-glass grant projection (mirror Governance.Service BreakGlassGrantDto). Active is
// IsActive(now); RequiresReview is true once the grant has expired without a review; Status is the
// derived lifecycle label ("active", "pending-review", or "reviewed") so a caller sees expiry + the
// mandatory-review obligation enforced at read time.
public sealed record BreakGlassGrantResponse(
    Guid Id,
    string PrincipalId,
    string TenantCode,
    string Action,
    string Justification,
    DateTimeOffset GrantedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ReviewedAt,
    string? ReviewedBy,
    string? ReviewOutcome,
    bool Active,
    bool RequiresReview,
    string Status);

// A delegation grant projection (mirror Governance.Service DelegationGrantDto). Active is
// IsActive(now); Status is the derived lifecycle label ("active", "expired", or "revoked").
public sealed record DelegationGrantResponse(
    Guid Id,
    string ManagerId,
    string DelegateId,
    string TenantCode,
    string[] Scopes,
    DateTimeOffset GrantedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    string? RevokedBy,
    bool Active,
    string Status);

// ---- Authz.Pdp native AuthZEN contract (mirror Authz.Pdp/Contracts/*.cs) ----

public sealed record PdpSubjectDto(
    string Type,
    string Id,
    IReadOnlyList<string> Roles,
    string? Tenant = null,
    string? Branch = null,
    PdpActorDto? Actor = null);

// The non-human on-behalf-of (OBO) delegate carried on PdpSubjectDto.Actor, mirroring the PDP
// Authz.Pdp/Contracts/Actor.cs shape (Type "agent"|"service", Id, delegated agent.bank.* Scopes).
// null Actor => a direct/human call; non-null => the human Subject is being acted for BY this
// delegate, and the PDP constrains the decision to the intersection of the human's rights and
// the delegate's scopes. Added as a trailing defaulted member so every existing positional
// PdpSubjectDto construction keeps compiling and the direct/human wire shape is unchanged.
public sealed record PdpActorDto(string Type, string Id, IReadOnlyList<string> Scopes);

public sealed record PdpActionDto(string Name);

public sealed record PdpResourceDto(
    string Type,
    string? Id = null,
    string? Tenant = null,
    string? Branch = null,
    decimal? Amount = null,
    string? MakerId = null,
    string? Status = null);

// CS21 (break-glass / delegation) extends the AuthZEN context ADDITIVELY, mirroring the PDP
// Authz.Pdp/Contracts/EvaluationContext.cs shape EXACTLY so System.Text.Json (which binds by
// property name) round-trips the request into the PDP unchanged: an optional break-glass
// emergency-elevation grant, an optional manager->delegate delegation grant, and the injected
// decision clock (Now) the PDP uses as the single source of "the current time" for expiry. All
// three are trailing defaulted members, so every existing positional new PdpContextDto(scopes)
// keeps compiling and the human/no-context wire shape is byte-identical.
public sealed record PdpContextDto(
    IReadOnlyList<string> Scopes,
    PdpBreakGlassGrantDto? BreakGlass = null,
    PdpDelegationGrantDto? Delegation = null,
    DateTimeOffset? Now = null);

// A break-glass emergency-elevation grant carried on PdpContextDto.BreakGlass, mirroring the PDP
// Authz.Pdp/Contracts/BreakGlassGrant.cs shape (GrantId, SubjectId, Action, ExpiresAt,
// Justification) by property name. The PDP raises a base Deny for a MISSING CAPABILITY
// (MissingScope / RoleNotAuthorized) to a Permit only when this grant names the request's subject
// and action and has not expired against Context.Now — it never overrides an integrity invariant.
public sealed record PdpBreakGlassGrantDto(
    string GrantId,
    string SubjectId,
    string Action,
    DateTimeOffset ExpiresAt,
    string Justification);

// A manager->delegate delegation grant carried on PdpContextDto.Delegation, mirroring the PDP
// Authz.Pdp/Contracts/DelegationGrant.cs shape (GrantId, ManagerId, DelegateId, ExpiresAt, Scopes) by
// property name. When present the PDP additionally requires it to be active and matching (ManagerId ==
// Subject.Id, DelegateId == Actor.Id, Now < ExpiresAt) AND the action's required scope to be present in
// Scopes (the manager's grant bounds the delegate, distinct from the Actor's own token) on top of the
// CS19 OBO intersection, else it denies DelegationNotActive / DelegationScopeMissing.
public sealed record PdpDelegationGrantDto(
    string GrantId,
    string ManagerId,
    string DelegateId,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string> Scopes);

public sealed record PdpAccessRequestDto(
    PdpSubjectDto Subject,
    PdpActionDto Action,
    PdpResourceDto Resource,
    PdpContextDto Context);

public sealed record PdpReasonDto(string Code, string Message);

public sealed record PdpObligationDto(
    string Id,
    IReadOnlyDictionary<string, string>? Properties = null);

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
    IReadOnlyList<PdpObligationDto> Obligations,
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
// RequestSnapshot (CS36) is the canonical JSON of the original AccessRequest when the row carries
// one — used for a faithful "Replay in Playground" pre-fill; null for rows without a snapshot. It
// is NON-hashed (reconstructed replay context), not part of the tamper-evident chain.
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
    string RowHash,
    string? RequestSnapshot = null);

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
