namespace AuthzEntitlements.Compliance;

// Thrown when the command line is invalid (unknown flag, missing value, bad URL, etc.). The tool
// maps this to a clear stderr message and exit code 2 — fail closed rather than guessing.
public sealed class OptionsParseException : Exception
{
    public OptionsParseException(string message)
        : base(message)
    {
    }
}

// The parsed, validated command-line options for the compliance report generator, plus the
// hand-rolled parser (no third-party arg library — zero new dependencies). Every value-taking flag
// guards its next token: a missing value or a following "-flag" is rejected with a clear error.
public sealed class ComplianceOptions
{
    // Directory the report files are written to; null means "print to stdout only".
    public string? OutputDir { get; init; }

    // Base URL of a live Governance service to probe; null means the live sections self-skip.
    public string? GovernanceUrl { get; init; }

    // The seeded principal probed for grants in the least-privilege section.
    public string PrincipalId { get; init; } = LeastPrivilegeReporter.DefaultPrincipalId;

    // When true, usage was requested; the tool prints help and exits 0.
    public bool Help { get; init; }

    // Parses argv into validated options. Sets Help for --help/-h. Throws OptionsParseException on
    // any invalid input (the caller maps that to exit code 2).
    public static ComplianceOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? outputDir = null;
        string? governanceUrl = null;
        var principalId = LeastPrivilegeReporter.DefaultPrincipalId;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                    return new ComplianceOptions { Help = true };

                case "--output":
                    outputDir = RequireValue(args, ref i);
                    break;

                case "--governance-url":
                    governanceUrl = ParseUrl(RequireValue(args, ref i));
                    break;

                case "--principal":
                    principalId = RequireValue(args, ref i);
                    break;

                default:
                    throw new OptionsParseException(
                        $"Unknown argument '{arg}'. Run with --help for usage.");
            }
        }

        return new ComplianceOptions
        {
            OutputDir = outputDir,
            GovernanceUrl = governanceUrl,
            PrincipalId = principalId,
        };
    }

    // Consumes the token after a value-taking flag, rejecting a missing value or a following flag.
    private static string RequireValue(string[] args, ref int index)
    {
        var flag = args[index];
        if (index + 1 >= args.Length)
        {
            throw new OptionsParseException($"Option '{flag}' requires a value.");
        }

        var value = args[index + 1];
        if (value.StartsWith('-'))
        {
            throw new OptionsParseException(
                $"Option '{flag}' requires a value but was followed by '{value}'.");
        }

        index++;
        return value;
    }

    // Validates that a governance URL is a well-formed absolute http(s) URI, failing closed on a
    // relative or non-HTTP value rather than letting HttpClient throw an opaque error later.
    private static string ParseUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new OptionsParseException(
                $"Option '--governance-url' requires an absolute http(s) URL, got '{value}'.");
        }

        return value;
    }

    // The usage text printed for --help.
    public static string UsageText() =>
        """
        Compliance evidence report generator.

        Produces a compliance evidence pack with four sections: a deterministic
        segregation-of-duties report, a deterministic audit-integrity report, and two
        live-probe reports (access-certification and least-privilege attestation) that
        self-skip when the Governance service is offline.

        Usage:
          dotnet run --project src/AuthzEntitlements.Compliance -- [options]

        Options:
          --output <dir>            Write compliance-report.json and compliance-report.md to <dir>
                                    (default: print the Markdown pack to stdout only).
          --governance-url <url>    Base URL of a live Governance service to probe for the
                                    certification + least-privilege sections (default: self-skip).
          --principal <id>          Seeded principal probed for grants (default: user-teller1).
          --help, -h                Print this help and exit.
        """;
}
