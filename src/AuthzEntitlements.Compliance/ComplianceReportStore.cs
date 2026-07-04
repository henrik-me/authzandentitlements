using System.Diagnostics;
using System.Text.Json;

namespace AuthzEntitlements.Compliance;

// Thrown when a compliance artefact cannot be loaded because it is missing, empty, malformed, or
// carries an unsupported schema version, OR when a live governance response that WAS received
// cannot be parsed. The tool fails closed on bad data (clear message + non-zero exit) rather than
// silently proceeding with a default or a half-parsed document.
public sealed class ComplianceDataException : Exception
{
    public ComplianceDataException(string message)
        : base(message)
    {
    }

    public ComplianceDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

// Persists compliance reports to JSON (and companion Markdown) files and loads reports back,
// failing closed on any malformed input. Also captures the current git SHA best-effort so a
// persisted report is traceable to a commit.
public static class ComplianceReportStore
{
    public const string JsonFileName = "compliance-report.json";
    public const string MarkdownFileName = "compliance-report.md";

    // Writes the report as compliance-report.json and compliance-report.md under outputDir (created
    // if absent) and returns the full path of the JSON file. Fixed filenames (not timestamped) so a
    // CI job always publishes the latest evidence pack at a stable path.
    public static string Save(ComplianceReport report, string outputDir)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);

        Directory.CreateDirectory(outputDir);

        var jsonPath = Path.Combine(outputDir, JsonFileName);
        var json = JsonSerializer.Serialize(report, ComplianceJson.Options);
        File.WriteAllText(jsonPath, json);

        var markdownPath = Path.Combine(outputDir, MarkdownFileName);
        File.WriteAllText(markdownPath, MarkdownRenderer.Render(report));

        return jsonPath;
    }

    // Loads a ComplianceReport from a JSON file, failing closed with a ComplianceDataException on a
    // missing file, empty content, malformed JSON, a null document, or an unsupported schemaVersion.
    public static ComplianceReport Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new ComplianceDataException($"Compliance report not found: '{path}'.");
        }

        var json = File.ReadAllText(path);
        return Parse(json, path);
    }

    // Parses a ComplianceReport from a JSON string, failing closed on malformed/null/unsupported
    // input. Exposed so callers (and tests) can validate in-memory content with the same contract.
    public static ComplianceReport Parse(string json, string source)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ComplianceDataException($"Compliance report from '{source}' was empty.");
        }

        ComplianceReport? report;
        try
        {
            report = JsonSerializer.Deserialize<ComplianceReport>(json, ComplianceJson.Options);
        }
        catch (JsonException ex)
        {
            throw new ComplianceDataException(
                $"Compliance report from '{source}' is not valid JSON: {ex.Message}", ex);
        }

        if (report is null)
        {
            throw new ComplianceDataException(
                $"Compliance report from '{source}' deserialized to null.");
        }

        // Fail closed on an incompatible schema: System.Text.Json silently ignores unknown fields
        // and defaults missing ones, so a newer/broken artifact would otherwise be read as nonsense
        // instead of rejected.
        if (report.SchemaVersion != ComplianceReport.CurrentSchemaVersion)
        {
            throw new ComplianceDataException(
                $"Compliance report from '{source}' has unsupported schemaVersion {report.SchemaVersion} " +
                $"(expected {ComplianceReport.CurrentSchemaVersion}).");
        }

        return report;
    }

    // Best-effort short git SHA of the current HEAD. Returns "unknown" when git is unavailable or
    // the command fails — capturing provenance must never break report generation.
    public static string CaptureGitSha()
    {
        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse --short HEAD")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return "unknown";
            }

            // Wait for exit FIRST with a hard timeout: reading StandardOutput before the process has
            // exited would block indefinitely if git hangs, defeating the timeout. `git rev-parse
            // --short HEAD` emits a few bytes (far under the OS pipe buffer), so reading after exit
            // cannot deadlock on a full buffer.
            if (!process.WaitForExit(2000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // best-effort kill; fall through to "unknown".
                }

                return "unknown";
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            return process.ExitCode == 0 && output.Length > 0
                ? output
                : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
