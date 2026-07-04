using System.Globalization;

namespace AuthzEntitlements.Compliance;

// Produces the least-privilege attestation section by probing a live Governance service for its
// access-package catalog and a representative principal's grants, attesting active vs expired
// (time-bound / JIT) access. Self-skips (collected = false) when the service is offline; a
// REACHED-but-malformed response fails closed via ComplianceDataException (raised by the client).
public static class LeastPrivilegeReporter
{
    // The default seeded principal probed for grants (matches GovernanceSeeder's user-teller1).
    public const string DefaultPrincipalId = "user-teller1";

    public static LeastPrivilegeSection Offline(string reason, string reproductionCommand) =>
        new(
            Collected: false,
            Reason: reason,
            ReproductionCommand: reproductionCommand,
            ProbedPrincipalId: null,
            AccessPackages: [],
            Grants: []);

    public static async Task<LeastPrivilegeSection> CollectAsync(
        IGovernanceClient client,
        string reproductionCommand,
        string principalId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);

        IReadOnlyList<AccessPackageDto> packages;
        IReadOnlyList<AccessGrantDto> grants;
        try
        {
            packages = await client.GetAccessPackagesAsync(cancellationToken).ConfigureAwait(false);
            grants = await client.GetPrincipalGrantsAsync(principalId, cancellationToken).ConfigureAwait(false);
        }
        catch (GovernanceUnreachableException ex)
        {
            return Offline($"governance service offline: {ex.Message}", reproductionCommand);
        }

        return new LeastPrivilegeSection(
            Collected: true,
            Reason: null,
            ReproductionCommand: reproductionCommand,
            ProbedPrincipalId: principalId,
            AccessPackages: packages.Select(ToPackageSummary).ToList(),
            Grants: grants.Select(g => ToAttestation(principalId, g)).ToList());
    }

    public static string ReproductionCommand(string? governanceUrl, string principalId)
    {
        var baseUrl = string.IsNullOrWhiteSpace(governanceUrl) ? "<governance-url>" : governanceUrl;
        return
            $"aspire run; curl {baseUrl}/api/governance/access-packages; " +
            $"curl {baseUrl}/api/governance/principals/{principalId}/grants; " +
            $"then re-run: dotnet run --project src/AuthzEntitlements.Compliance -- " +
            $"--governance-url {baseUrl} --principal {principalId}";
    }

    private static AccessPackageSummary ToPackageSummary(AccessPackageDto p) =>
        new(
            Code: p.Code,
            DisplayName: p.DisplayName,
            RequiresApproval: p.RequiresApproval,
            DefaultDurationMinutes: p.DefaultDurationMinutes,
            Roles: p.Roles ?? []);

    private static GrantAttestation ToAttestation(string principalId, AccessGrantDto g) =>
        new(
            Id: g.Id.ToString(),
            PrincipalId: string.IsNullOrWhiteSpace(g.PrincipalId) ? principalId : g.PrincipalId,
            AccessPackageCode: g.AccessPackageCode,
            Status: g.Status,
            Active: g.Active,
            GrantedAt: g.GrantedAt.ToString("O", CultureInfo.InvariantCulture),
            ExpiresAt: g.ExpiresAt.ToString("O", CultureInfo.InvariantCulture));
}
