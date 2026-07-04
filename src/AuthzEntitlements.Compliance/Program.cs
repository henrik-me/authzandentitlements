using System.Globalization;
using AuthzEntitlements.Compliance;

// Entry point for the compliance evidence report generator. Parses options (fail closed → exit 2),
// builds the four-section evidence pack (two deterministic, two live-probe that self-skip offline),
// prints the Markdown pack to stdout, and — under --output — also writes compliance-report.json and
// compliance-report.md. A live Governance service that is offline never fails the run; only a
// malformed governance response (fail-closed parse) or a bad artifact does.

ComplianceOptions options;
try
{
    options = ComplianceOptions.Parse(args);
}
catch (OptionsParseException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 2;
}

if (options.Help)
{
    Console.WriteLine(ComplianceOptions.UsageText());
    return 0;
}

var generatedAtUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
var gitSha = ComplianceReportStore.CaptureGitSha();

HttpGovernanceClient? client = null;
try
{
    if (options.GovernanceUrl is not null)
    {
        client = new HttpGovernanceClient(options.GovernanceUrl);
    }

    ComplianceReport report;
    try
    {
        report = await ComplianceReportBuilder.BuildAsync(
            generatedAtUtc,
            gitSha,
            client,
            options.GovernanceUrl,
            options.PrincipalId);
    }
    catch (ComplianceDataException ex)
    {
        // A REACHED-but-malformed governance response fails closed with a clear error and a
        // non-zero exit — never a silent partial report.
        Console.Error.WriteLine($"error: {ex.Message}");
        return 1;
    }

    Console.WriteLine(MarkdownRenderer.Render(report));

    if (options.OutputDir is not null)
    {
        var jsonPath = ComplianceReportStore.Save(report, options.OutputDir);
        var markdownPath = Path.Combine(options.OutputDir, ComplianceReportStore.MarkdownFileName);
        Console.WriteLine($"Report written to {jsonPath} and {markdownPath}");
    }

    return 0;
}
finally
{
    client?.Dispose();
}
