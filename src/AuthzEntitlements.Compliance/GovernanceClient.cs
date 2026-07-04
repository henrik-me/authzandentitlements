namespace AuthzEntitlements.Compliance;

// Raw response shapes deserialized from the Governance service (a subset of its DTOs — only the
// fields the compliance probes consume). Property names match the service's camelCase JSON, which
// ComplianceJson.Options (Web defaults) binds case-insensitively.

public sealed record ReviewItemDto(string Decision);

public sealed record ReviewCampaignDto(
    Guid Id,
    string Name,
    string TenantCode,
    string Status,
    IReadOnlyList<ReviewItemDto>? Items);

public sealed record AccessPackageDto(
    string Code,
    string DisplayName,
    bool RequiresApproval,
    int DefaultDurationMinutes,
    IReadOnlyList<string>? Roles);

public sealed record AccessGrantDto(
    Guid Id,
    string PrincipalId,
    string AccessPackageCode,
    string Status,
    bool Active,
    DateTimeOffset GrantedAt,
    DateTimeOffset ExpiresAt);

// Signals that a live Governance probe could not REACH the service (connection refused, DNS
// failure, or timeout). The live-probe reporters treat this as a self-skip (collected=false),
// never a run failure — mirroring the repo's "live-engine tests self-skip offline" convention. It
// is distinct from ComplianceDataException, which a REACHED service raises for a non-success HTTP
// status or a malformed response (fail-closed, surfaces as a non-zero exit).
public sealed class GovernanceUnreachableException : Exception
{
    public GovernanceUnreachableException(string message)
        : base(message)
    {
    }

    public GovernanceUnreachableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

// The read-only Governance surface the compliance probes need. An interface so the live-probe
// reporters are unit-testable with an in-memory fake and the real HTTP client is the only piece
// that touches the network.
public interface IGovernanceClient
{
    Task<IReadOnlyList<ReviewCampaignDto>> GetCampaignsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AccessPackageDto>> GetAccessPackagesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AccessGrantDto>> GetPrincipalGrantsAsync(
        string principalId, CancellationToken cancellationToken);
}
