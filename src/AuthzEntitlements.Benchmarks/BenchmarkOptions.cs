namespace AuthzEntitlements.Benchmarks;

// Thrown when the command line is invalid (unknown flag, missing value, bad engine name, etc.). The
// harness maps this to a clear stderr message and exit code 2 — fail closed rather than guessing.
public sealed class OptionsParseException : Exception
{
    public OptionsParseException(string message)
        : base(message)
    {
    }
}

// The parsed, validated command-line options for the benchmark harness, plus the hand-rolled parser
// (no third-party arg library — zero new dependencies). Every value-taking flag guards its next
// token: a missing value or a following "-flag" is rejected with a clear error.
public sealed class BenchmarkOptions
{
    public const int DefaultIterations = 10_000;
    public const int DefaultWarmup = 1_000;
    public const string DefaultOutDir = "benchmarks/results";
    public const string BaselineFileName = "pdp-latency-baseline.json";

    // Measured evaluations per engine.
    public int Iterations { get; init; } = DefaultIterations;

    // Discarded warmup evaluations per engine.
    public int Warmup { get; init; } = DefaultWarmup;

    // Engines to benchmark. Defaults to the four deterministic in-process engines.
    public IReadOnlyList<string> Engines { get; init; } = EngineCatalog.InProcessEngineNames;

    // Directory results are written to.
    public string OutDir { get; init; } = DefaultOutDir;

    // Baseline run to compare against under --check. Defaults to the committed baseline shipped
    // beside the app.
    public string BaselinePath { get; init; } = DefaultBaselinePath();

    // When true, compare the current run to the baseline and exit non-zero on any regression.
    public bool Check { get; init; }

    // When true, usage was requested; the harness prints help and exits 0.
    public bool Help { get; init; }

    // The default baseline path: the copy shipped next to the app so `--check` works out of the box.
    public static string DefaultBaselinePath() =>
        Path.Combine(AppContext.BaseDirectory, "baseline", BaselineFileName);

    // Parses argv into validated options. Sets Help for --help/-h. Throws OptionsParseException on
    // any invalid input (the caller maps that to exit code 2).
    public static BenchmarkOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var iterations = DefaultIterations;
        var warmup = DefaultWarmup;
        IReadOnlyList<string> engines = EngineCatalog.InProcessEngineNames;
        var outDir = DefaultOutDir;
        var baseline = DefaultBaselinePath();
        var check = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                    return new BenchmarkOptions { Help = true };

                case "--check":
                    check = true;
                    break;

                case "--iterations":
                    iterations = ParsePositiveInt(arg, RequireValue(args, ref i));
                    break;

                case "--warmup":
                    warmup = ParseNonNegativeInt(arg, RequireValue(args, ref i));
                    break;

                case "--engines":
                    engines = ParseEngines(RequireValue(args, ref i));
                    break;

                case "--out":
                    outDir = RequireValue(args, ref i);
                    break;

                case "--baseline":
                    baseline = RequireValue(args, ref i);
                    break;

                default:
                    throw new OptionsParseException(
                        $"Unknown argument '{arg}'. Run with --help for usage.");
            }
        }

        return new BenchmarkOptions
        {
            Iterations = iterations,
            Warmup = warmup,
            Engines = engines,
            OutDir = outDir,
            BaselinePath = baseline,
            Check = check,
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

    private static int ParsePositiveInt(string flag, string value)
    {
        if (!int.TryParse(value, out var parsed) || parsed <= 0)
        {
            throw new OptionsParseException(
                $"Option '{flag}' requires a positive integer, got '{value}'.");
        }

        return parsed;
    }

    private static int ParseNonNegativeInt(string flag, string value)
    {
        if (!int.TryParse(value, out var parsed) || parsed < 0)
        {
            throw new OptionsParseException(
                $"Option '{flag}' requires a non-negative integer, got '{value}'.");
        }

        return parsed;
    }

    // Parses the --engines CSV. "all" expands to every known engine (in-process + live). Each name
    // must be a known engine; an unknown name fails closed.
    private static IReadOnlyList<string> ParseEngines(string value)
    {
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new OptionsParseException("Option '--engines' requires at least one engine name.");
        }

        if (parts.Length == 1 && string.Equals(parts[0], "all", StringComparison.OrdinalIgnoreCase))
        {
            return EngineCatalog.AllEngineNames;
        }

        var selected = new List<string>(parts.Length);
        foreach (var part in parts)
        {
            var match = EngineCatalog.AllEngineNames
                .FirstOrDefault(n => string.Equals(n, part, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                throw new OptionsParseException(
                    $"Unknown engine '{part}'. Known engines: {string.Join(", ", EngineCatalog.AllEngineNames)}, or 'all'.");
            }

            // Fail closed on duplicates rather than silently running an engine twice (which would
            // emit duplicate engineName entries in the persisted run).
            if (selected.Contains(match))
            {
                throw new OptionsParseException(
                    $"Duplicate engine '{match}' in '--engines'. List each engine at most once.");
            }

            selected.Add(match);
        }

        return selected;
    }

    // The usage text printed for --help.
    public static string UsageText() =>
        """
        PDP performance benchmark harness.

        Usage:
          dotnet run --project src/AuthzEntitlements.Benchmarks -- [options]

        Options:
          --iterations <n>   Measured evaluations per engine (default 10000).
          --warmup <n>       Discarded warmup evaluations per engine (default 1000).
          --engines <csv>    Engines to run: reference,aspnet,casbin,cedar,opa,openfga or 'all'
                             (default: the four in-process engines; live engines self-skip offline).
          --out <dir>        Directory results JSON is written to (default benchmarks/results).
          --baseline <path>  Baseline run for --check (default: the committed baseline).
          --check            Compare the run to the baseline; exit non-zero on a regression.
          --help, -h         Print this help and exit.
        """;
}
