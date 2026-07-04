namespace AuthzEntitlements.Compliance;

// Produces the access-certification evidence section by probing a live Governance service for its
// review campaigns and summarising each campaign's recertification decisions. Self-skips (collected
// = false) when the service is offline; a REACHED-but-malformed response fails closed via
// ComplianceDataException (raised by the client) and is intentionally NOT caught here.
public static class CertificationReporter
{
    private const string Certify = "Certify";
    private const string Revoke = "Revoke";

    // The offline section: the service was not supplied or was unreachable.
    public static CertificationSection Offline(string reason, string reproductionCommand) =>
        new(Collected: false, Reason: reason, ReproductionCommand: reproductionCommand, Campaigns: []);

    public static async Task<CertificationSection> CollectAsync(
        IGovernanceClient client, string reproductionCommand, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);

        IReadOnlyList<ReviewCampaignDto> campaigns;
        try
        {
            campaigns = await client.GetCampaignsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (GovernanceUnreachableException ex)
        {
            return Offline($"governance service offline: {ex.Message}", reproductionCommand);
        }

        var summaries = campaigns.Select(ToSummary).ToList();
        return new CertificationSection(
            Collected: true,
            Reason: null,
            ReproductionCommand: reproductionCommand,
            Campaigns: summaries);
    }

    // The exact reproduction command to collect this section live. When no URL is known a
    // placeholder is used so the instruction still reads correctly.
    public static string ReproductionCommand(string? governanceUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(governanceUrl) ? "<governance-url>" : governanceUrl;
        return
            $"aspire run; curl {baseUrl}/api/governance/review-campaigns; " +
            $"then re-run: dotnet run --project src/AuthzEntitlements.Compliance -- --governance-url {baseUrl}";
    }

    private static CampaignSummary ToSummary(ReviewCampaignDto campaign)
    {
        var items = campaign.Items ?? [];
        var certified = items.Count(i => string.Equals(i.Decision, Certify, StringComparison.Ordinal));
        var revoked = items.Count(i => string.Equals(i.Decision, Revoke, StringComparison.Ordinal));
        var pending = items.Count - certified - revoked;

        return new CampaignSummary(
            Id: campaign.Id.ToString(),
            Name: campaign.Name,
            TenantCode: campaign.TenantCode,
            Status: campaign.Status,
            TotalItems: items.Count,
            Certified: certified,
            Revoked: revoked,
            Pending: pending);
    }
}
