namespace AuthzEntitlements.Compliance;

// Assembles a full ComplianceReport. The two deterministic sections (SoD, audit-integrity) are
// always produced DB-free; the two live sections self-skip (collected = false) when no Governance
// client is supplied, and otherwise probe the service. A REACHED-but-malformed governance response
// fails closed (ComplianceDataException propagates) rather than being silently swallowed.
public static class ComplianceReportBuilder
{
    public static async Task<ComplianceReport> BuildAsync(
        string generatedAtUtc,
        string gitSha,
        IGovernanceClient? client,
        string? governanceUrl,
        string principalId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(generatedAtUtc);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitSha);
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);

        var sod = SodEvidenceReporter.Build();
        var audit = AuditIntegrityReporter.Build();

        var certRepro = CertificationReporter.ReproductionCommand(governanceUrl);
        var lpRepro = LeastPrivilegeReporter.ReproductionCommand(governanceUrl, principalId);

        CertificationSection certification;
        LeastPrivilegeSection leastPrivilege;
        if (client is null)
        {
            const string reason = "no --governance-url supplied (deterministic sections only)";
            certification = CertificationReporter.Offline(reason, certRepro);
            leastPrivilege = LeastPrivilegeReporter.Offline(reason, lpRepro);
        }
        else
        {
            certification = await CertificationReporter
                .CollectAsync(client, certRepro, cancellationToken).ConfigureAwait(false);
            leastPrivilege = await LeastPrivilegeReporter
                .CollectAsync(client, lpRepro, principalId, cancellationToken).ConfigureAwait(false);
        }

        return new ComplianceReport(
            SchemaVersion: ComplianceReport.CurrentSchemaVersion,
            GeneratedAtUtc: generatedAtUtc,
            GitSha: gitSha,
            Sod: sod,
            AuditIntegrity: audit,
            Certification: certification,
            LeastPrivilege: leastPrivilege);
    }

    // Convenience for the deterministic-only report (no live probes) — used by tests and by the
    // default run when no --governance-url is supplied.
    public static ComplianceReport BuildDeterministic(
        string generatedAtUtc, string gitSha, string principalId) =>
        BuildAsync(generatedAtUtc, gitSha, client: null, governanceUrl: null, principalId)
            .GetAwaiter().GetResult();
}
