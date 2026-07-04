using System.Diagnostics;
using System.Text.Json;

namespace AuthzEntitlements.Benchmarks;

// Thrown when a benchmark artefact cannot be loaded because it is missing, empty, or malformed.
// The harness fails closed on a bad baseline/result file (clear message + non-zero exit) rather
// than silently proceeding with a default or a half-parsed run.
public sealed class BenchmarkDataException : Exception
{
    public BenchmarkDataException(string message)
        : base(message)
    {
    }

    public BenchmarkDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

// Persists benchmark runs to timestamped JSON files and loads runs (results or baselines) back,
// failing closed on any malformed input. Also captures the current git SHA best-effort so a
// persisted run is traceable to a commit.
public static class ResultStore
{
    // Writes the run to a timestamped file under outputDir (created if absent) and returns the full
    // path. The filename embeds the run's UTC timestamp (sanitised for the filesystem) so results
    // sort chronologically and never collide within a directory.
    public static string Save(BenchmarkRun run, string outputDir)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDir);

        Directory.CreateDirectory(outputDir);

        var stamp = SanitizeForFileName(run.TimestampUtc);
        var path = Path.Combine(outputDir, $"pdp-latency-{stamp}.json");
        var json = JsonSerializer.Serialize(run, BenchmarkJson.Options);
        File.WriteAllText(path, json);
        return path;
    }

    // Loads a BenchmarkRun from a JSON file, failing closed with a BenchmarkDataException on a
    // missing file, empty content, malformed JSON, or a null document.
    public static BenchmarkRun Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new BenchmarkDataException($"Benchmark file not found: '{path}'.");
        }

        var json = File.ReadAllText(path);
        return Parse(json, path);
    }

    // Parses a BenchmarkRun from a JSON string, failing closed on malformed or null input. Exposed
    // so callers (and tests) can validate in-memory content with the same fail-closed contract.
    public static BenchmarkRun Parse(string json, string source)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new BenchmarkDataException($"Benchmark data from '{source}' was empty.");
        }

        BenchmarkRun? run;
        try
        {
            run = JsonSerializer.Deserialize<BenchmarkRun>(json, BenchmarkJson.Options);
        }
        catch (JsonException ex)
        {
            throw new BenchmarkDataException(
                $"Benchmark data from '{source}' is not valid JSON: {ex.Message}", ex);
        }

        if (run is null)
        {
            throw new BenchmarkDataException(
                $"Benchmark data from '{source}' deserialized to null.");
        }

        // Fail closed on an incompatible schema: System.Text.Json silently ignores unknown fields and
        // defaults missing ones, so a newer/broken artifact would otherwise be compared as nonsense
        // instead of rejected.
        if (run.SchemaVersion != BenchmarkRun.CurrentSchemaVersion)
        {
            throw new BenchmarkDataException(
                $"Benchmark data from '{source}' has unsupported schemaVersion {run.SchemaVersion} " +
                $"(expected {BenchmarkRun.CurrentSchemaVersion}).");
        }

        return run;
    }

    // Best-effort short git SHA of the current HEAD. Returns "unknown" when git is unavailable or
    // the command fails — capturing provenance must never break a benchmark run.
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

            // Wait for exit FIRST with a hard timeout: reading StandardOutput before the process
            // has exited would block indefinitely if git hangs, defeating the timeout. `git rev-parse
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

    // Replaces filesystem-hostile characters (notably ':' from an ISO-8601 timestamp) with '-'.
    private static string SanitizeForFileName(string value)
    {
        var chars = value.ToCharArray();
        var invalid = Path.GetInvalidFileNameChars();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0 || chars[i] == ':')
            {
                chars[i] = '-';
            }
        }

        return new string(chars);
    }
}
