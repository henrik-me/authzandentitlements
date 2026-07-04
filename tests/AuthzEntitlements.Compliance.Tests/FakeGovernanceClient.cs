namespace AuthzEntitlements.Compliance.Tests;

// An in-memory IGovernanceClient for the live-probe reporters: returns canned data, or throws
// GovernanceUnreachableException (offline) / ComplianceDataException (reached-but-malformed) to
// exercise the self-skip vs fail-closed branches without a real server.
internal sealed class FakeGovernanceClient : IGovernanceClient
{
    public bool Unreachable { get; init; }

    public bool Malformed { get; init; }

    public IReadOnlyList<ReviewCampaignDto> Campaigns { get; init; } = [];

    public IReadOnlyList<AccessPackageDto> AccessPackages { get; init; } = [];

    public IReadOnlyList<AccessGrantDto> Grants { get; init; } = [];

    public Task<IReadOnlyList<ReviewCampaignDto>> GetCampaignsAsync(CancellationToken cancellationToken) =>
        Result(Campaigns);

    public Task<IReadOnlyList<AccessPackageDto>> GetAccessPackagesAsync(CancellationToken cancellationToken) =>
        Result(AccessPackages);

    public Task<IReadOnlyList<AccessGrantDto>> GetPrincipalGrantsAsync(
        string principalId, CancellationToken cancellationToken) => Result(Grants);

    private Task<IReadOnlyList<T>> Result<T>(IReadOnlyList<T> value)
    {
        if (Unreachable)
        {
            throw new GovernanceUnreachableException("connection refused (fake)");
        }

        if (Malformed)
        {
            throw new ComplianceDataException("Governance response is not valid JSON: fake.");
        }

        return Task.FromResult(value);
    }
}
