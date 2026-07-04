using System.Text;

namespace AuthzEntitlements.Compliance;

// Renders a ComplianceReport as a human-readable Markdown evidence pack. Pure and deterministic
// (no I/O) so the exact output is unit-testable. The deterministic sections always show populated
// evidence; the live sections show either collected data or a self-skip block with the exact
// reproduction command.
public static class MarkdownRenderer
{
    public static string Render(ComplianceReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sb = new StringBuilder();
        sb.AppendLine("# Compliance evidence pack");
        sb.AppendLine();
        sb.AppendLine($"- Schema version: {report.SchemaVersion}");
        sb.AppendLine($"- Generated (UTC): {report.GeneratedAtUtc}");
        sb.AppendLine($"- Git SHA: {report.GitSha}");
        sb.AppendLine();

        RenderSod(sb, report.Sod);
        RenderAudit(sb, report.AuditIntegrity);
        RenderCertification(sb, report.Certification);
        RenderLeastPrivilege(sb, report.LeastPrivilege);

        return sb.ToString();
    }

    private static void RenderSod(StringBuilder sb, SodEvidenceSection sod)
    {
        sb.AppendLine("## 1. Segregation-of-duties evidence (deterministic)");
        sb.AppendLine();
        sb.AppendLine(
            $"Incompatible pairs enforced: {sod.IncompatiblePairCount}; cases evaluated: " +
            $"{sod.CasesEvaluated}; conflicts detected: {sod.ConflictsDetected}. " +
            $"All toxic combinations denied: {YesNo(sod.AllToxicCombinationsDenied)}; " +
            $"all clean sets permitted: {YesNo(sod.AllCleanSetsPermitted)}.");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Proposed roles | Conflict | Detected pair | Decision | Reason | Pass |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var c in sod.Cases)
        {
            var pair = c.DetectedPairFirst is null ? "—" : $"{c.DetectedPairFirst} + {c.DetectedPairSecond}";
            var roles = c.ProposedRoles.Count == 0 ? "(none)" : string.Join(", ", c.ProposedRoles);
            sb.AppendLine(
                $"| {c.Scenario} | {roles} | {YesNo(c.ConflictDetected)} | {pair} | " +
                $"{c.Decision} | {c.ReasonCode} | {PassFail(c.Passed)} |");
        }

        sb.AppendLine();
        RenderControls(sb, sod.MappedControls);
    }

    private static void RenderAudit(StringBuilder sb, AuditIntegritySection audit)
    {
        sb.AppendLine("## 2. Audit-integrity evidence (deterministic)");
        sb.AppendLine();
        sb.AppendLine(
            $"Sample chain length: {audit.ChainLength}; baseline chain valid: " +
            $"{YesNo(audit.BaselineChainValid)}; tamper cases: {audit.TamperCasesEvaluated}; " +
            $"detected: {audit.TamperCasesDetected}. All tamper detected: " +
            $"{YesNo(audit.AllTamperDetected)}.");
        sb.AppendLine();
        sb.AppendLine("| Scenario | Expected detected | Valid | Broken at | Reason | Pass |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var c in audit.Cases)
        {
            var brokenAt = c.BrokenAtSequence?.ToString() ?? "—";
            var reason = c.Reason ?? "—";
            sb.AppendLine(
                $"| {c.Scenario} | {YesNo(c.ExpectedDetected)} | {YesNo(c.Valid)} | {brokenAt} | " +
                $"{reason} | {PassFail(c.Passed)} |");
        }

        sb.AppendLine();
        RenderControls(sb, audit.MappedControls);
    }

    private static void RenderCertification(StringBuilder sb, CertificationSection cert)
    {
        sb.AppendLine("## 3. Access-certification evidence (live probe)");
        sb.AppendLine();
        if (!cert.Collected)
        {
            RenderSkip(sb, cert.Reason, cert.ReproductionCommand);
            return;
        }

        if (cert.Campaigns.Count == 0)
        {
            sb.AppendLine("Collected: yes. No review campaigns found.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Campaign | Tenant | Status | Items | Certified | Revoked | Pending |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var c in cert.Campaigns)
        {
            sb.AppendLine(
                $"| {c.Name} | {c.TenantCode} | {c.Status} | {c.TotalItems} | {c.Certified} | " +
                $"{c.Revoked} | {c.Pending} |");
        }

        sb.AppendLine();
    }

    private static void RenderLeastPrivilege(StringBuilder sb, LeastPrivilegeSection lp)
    {
        sb.AppendLine("## 4. Least-privilege attestation (live probe)");
        sb.AppendLine();
        if (!lp.Collected)
        {
            RenderSkip(sb, lp.Reason, lp.ReproductionCommand);
            return;
        }

        sb.AppendLine($"Probed principal: {lp.ProbedPrincipalId}");
        sb.AppendLine();
        sb.AppendLine("### Access packages");
        sb.AppendLine();
        sb.AppendLine("| Code | Display name | Requires approval | Default minutes | Roles |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var p in lp.AccessPackages)
        {
            var roles = p.Roles.Count == 0 ? "(none)" : string.Join(", ", p.Roles);
            sb.AppendLine(
                $"| {p.Code} | {p.DisplayName} | {YesNo(p.RequiresApproval)} | " +
                $"{p.DefaultDurationMinutes} | {roles} |");
        }

        sb.AppendLine();
        sb.AppendLine("### Grants (time-bound / JIT)");
        sb.AppendLine();
        if (lp.Grants.Count == 0)
        {
            sb.AppendLine("No grants for the probed principal.");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Package | Status | Active | Granted at | Expires at |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var g in lp.Grants)
        {
            sb.AppendLine(
                $"| {g.AccessPackageCode} | {g.Status} | {YesNo(g.Active)} | {g.GrantedAt} | {g.ExpiresAt} |");
        }

        sb.AppendLine();
    }

    private static void RenderControls(StringBuilder sb, IReadOnlyList<MappedControl> controls)
    {
        if (controls.Count == 0)
        {
            return;
        }

        sb.AppendLine("Mapped controls:");
        sb.AppendLine();
        sb.AppendLine("| Control | Name | Framework | Enforcement point | Evidence |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var c in controls)
        {
            sb.AppendLine(
                $"| {c.ControlId} | {c.Name} | {c.Framework} | {c.EnforcementPoint} | {c.Evidence} |");
        }

        sb.AppendLine();
    }

    private static void RenderSkip(StringBuilder sb, string? reason, string reproductionCommand)
    {
        sb.AppendLine($"Collected: no — {reason ?? "governance service offline"}.");
        sb.AppendLine();
        sb.AppendLine("Reproduce under a running stack:");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(reproductionCommand);
        sb.AppendLine("```");
        sb.AppendLine();
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    private static string PassFail(bool value) => value ? "PASS" : "FAIL";
}
