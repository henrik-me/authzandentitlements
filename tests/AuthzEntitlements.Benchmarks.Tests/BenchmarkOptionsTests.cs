using AuthzEntitlements.Benchmarks;
using Xunit;

namespace AuthzEntitlements.Benchmarks.Tests;

// Covers the hand-rolled argument parser: defaults, flag handling, engine expansion, --help, and the
// fail-closed cases (missing value, following flag, unknown flag/engine) that map to exit code 2.
public sealed class BenchmarkOptionsTests
{
    [Fact]
    public void Parse_NoArgs_UsesDefaults()
    {
        var options = BenchmarkOptions.Parse([]);

        Assert.False(options.Help);
        Assert.False(options.Check);
        Assert.Equal(BenchmarkOptions.DefaultIterations, options.Iterations);
        Assert.Equal(BenchmarkOptions.DefaultWarmup, options.Warmup);
        Assert.Equal(EngineCatalog.InProcessEngineNames, options.Engines);
        Assert.Equal(BenchmarkOptions.DefaultOutDir, options.OutDir);
        Assert.Contains("baseline", options.BaselinePath);
    }

    [Fact]
    public void Parse_Check_SetsCheckFlag()
    {
        var options = BenchmarkOptions.Parse(["--check"]);

        Assert.True(options.Check);
    }

    [Fact]
    public void Parse_Help_SetsHelpFlag()
    {
        Assert.True(BenchmarkOptions.Parse(["--help"]).Help);
        Assert.True(BenchmarkOptions.Parse(["-h"]).Help);
    }

    [Fact]
    public void Parse_IterationsAndWarmup_AreParsed()
    {
        var options = BenchmarkOptions.Parse(["--iterations", "500", "--warmup", "50"]);

        Assert.Equal(500, options.Iterations);
        Assert.Equal(50, options.Warmup);
    }

    [Fact]
    public void Parse_EnginesCsv_SelectsNamedEngines()
    {
        var options = BenchmarkOptions.Parse(["--engines", "reference,casbin"]);

        Assert.Equal(["reference", "casbin"], options.Engines);
    }

    [Fact]
    public void Parse_EnginesAll_ExpandsToEveryKnownEngine()
    {
        var options = BenchmarkOptions.Parse(["--engines", "all"]);

        Assert.Equal(EngineCatalog.AllEngineNames, options.Engines);
        Assert.Contains("opa", options.Engines);
        Assert.Contains("openfga", options.Engines);
    }

    [Fact]
    public void Parse_OutAndBaseline_AreParsed()
    {
        var options = BenchmarkOptions.Parse(["--out", "myresults", "--baseline", "base.json"]);

        Assert.Equal("myresults", options.OutDir);
        Assert.Equal("base.json", options.BaselinePath);
    }

    [Fact]
    public void Parse_MissingValueForFlag_Throws()
    {
        Assert.Throws<OptionsParseException>(() => BenchmarkOptions.Parse(["--iterations"]));
    }

    [Fact]
    public void Parse_FlagFollowedByAnotherFlag_Throws()
    {
        Assert.Throws<OptionsParseException>(() => BenchmarkOptions.Parse(["--out", "--check"]));
    }

    [Fact]
    public void Parse_UnknownFlag_Throws()
    {
        Assert.Throws<OptionsParseException>(() => BenchmarkOptions.Parse(["--bogus"]));
    }

    [Fact]
    public void Parse_UnknownEngine_Throws()
    {
        Assert.Throws<OptionsParseException>(() => BenchmarkOptions.Parse(["--engines", "nope"]));
    }

    [Fact]
    public void Parse_NonPositiveIterations_Throws()
    {
        Assert.Throws<OptionsParseException>(() => BenchmarkOptions.Parse(["--iterations", "0"]));
    }

    [Fact]
    public void Parse_ZeroWarmup_IsAllowed()
    {
        var options = BenchmarkOptions.Parse(["--warmup", "0"]);

        Assert.Equal(0, options.Warmup);
    }
}
