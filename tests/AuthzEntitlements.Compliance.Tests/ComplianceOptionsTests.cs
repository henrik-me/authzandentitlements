using Xunit;

namespace AuthzEntitlements.Compliance.Tests;

// Covers CLI parsing: value-flag guarding (missing / "-"-prefixed value), URL validation, defaults,
// and --help.
public sealed class ComplianceOptionsTests
{
    [Fact]
    public void Help_Flag_SetsHelp()
    {
        Assert.True(ComplianceOptions.Parse(["--help"]).Help);
        Assert.True(ComplianceOptions.Parse(["-h"]).Help);
    }

    [Fact]
    public void Defaults_NoOutput_NoUrl_SeededPrincipal()
    {
        var options = ComplianceOptions.Parse([]);

        Assert.Null(options.OutputDir);
        Assert.Null(options.GovernanceUrl);
        Assert.Equal("user-teller1", options.PrincipalId);
        Assert.False(options.Help);
    }

    [Fact]
    public void Output_MissingValue_FailsClosed()
    {
        Assert.Throws<OptionsParseException>(() => ComplianceOptions.Parse(["--output"]));
    }

    [Fact]
    public void GovernanceUrl_FollowedByFlag_FailsClosed()
    {
        Assert.Throws<OptionsParseException>(
            () => ComplianceOptions.Parse(["--governance-url", "--principal"]));
    }

    [Fact]
    public void GovernanceUrl_Relative_FailsClosed()
    {
        Assert.Throws<OptionsParseException>(
            () => ComplianceOptions.Parse(["--governance-url", "not-a-url"]));
    }

    [Fact]
    public void GovernanceUrl_NonHttpScheme_FailsClosed()
    {
        Assert.Throws<OptionsParseException>(
            () => ComplianceOptions.Parse(["--governance-url", "ftp://example.com"]));
    }

    [Fact]
    public void GovernanceUrl_ValidHttp_IsParsed()
    {
        var options = ComplianceOptions.Parse(["--governance-url", "http://localhost:5300"]);
        Assert.Equal("http://localhost:5300", options.GovernanceUrl);
    }

    [Fact]
    public void Output_And_Principal_AreParsed()
    {
        var options = ComplianceOptions.Parse(["--output", "./out", "--principal", "user-manager1"]);

        Assert.Equal("./out", options.OutputDir);
        Assert.Equal("user-manager1", options.PrincipalId);
    }

    [Fact]
    public void UnknownArgument_FailsClosed()
    {
        Assert.Throws<OptionsParseException>(() => ComplianceOptions.Parse(["--nope"]));
    }

    [Fact]
    public void UsageText_MentionsEveryFlag()
    {
        var usage = ComplianceOptions.UsageText();

        Assert.Contains("--output", usage);
        Assert.Contains("--governance-url", usage);
        Assert.Contains("--principal", usage);
        Assert.Contains("--help", usage);
    }
}
