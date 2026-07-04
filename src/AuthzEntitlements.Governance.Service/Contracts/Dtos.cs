namespace AuthzEntitlements.Governance.Service.Contracts;

// Request/response DTOs for the governance API. ASP.NET's web JSON defaults serialise
// property names camelCase. Enum-valued domain state (request status, SoD outcome, review
// decision, campaign status) is projected to strings on the way out so the wire shape
// stays a primitive/string contract.

// ---- Request bodies ----

// Body for POST /requests. RequestedDurationMinutes is optional; null means "use the
// package default".
public sealed record CreateAccessRequestBody(
    string PrincipalId,
    string AccessPackageCode,
    string Justification,
    int? RequestedDurationMinutes);

// Body for POST /requests/{id}/approve. ApproverId is the checker; it must differ from the
// request's requester (maker-checker).
public sealed record ApproveRequestBody(string ApproverId);

// Body for POST /requests/{id}/reject.
public sealed record RejectRequestBody(string ApproverId, string Reason);

// Body for POST /grants/{id}/revoke.
public sealed record RevokeGrantBody(string RevokedBy);

// Body for POST /review-campaigns.
public sealed record CreateCampaignBody(string Name, string TenantCode, DateTimeOffset DueAt);

// Body for POST /review-items/{id}/decision. Decision is "Certify" or "Revoke".
public sealed record ReviewItemDecisionBody(string Decision, string ReviewedBy);

// ---- Response bodies ----

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

// A grant projection. Active is IsActive(now); Status is the derived lifecycle label
// ("active", "expired", or "revoked") so a caller sees expiry enforced at read time
// without a background sweeper.
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

// The principal's effective access now: baseline roles UNION the roles from every
// currently-active grant.
public sealed record PrincipalAccessResponse(
    string PrincipalId,
    string TenantCode,
    string[] EffectiveRoles,
    string[] BaselineRoles,
    string[] ActiveGrantPackages);

public sealed record ReviewItemResponse(
    Guid Id,
    Guid CampaignId,
    Guid AccessGrantId,
    string PrincipalId,
    string Decision,
    string? ReviewedBy,
    DateTimeOffset? ReviewedAt);

public sealed record ReviewCampaignResponse(
    Guid Id,
    string Name,
    string TenantCode,
    DateTimeOffset CreatedAt,
    DateTimeOffset DueAt,
    string Status,
    ReviewItemResponse[] Items);

public sealed record CampaignRunResponse(Guid CampaignId, int ItemsCreated);
