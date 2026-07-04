using AuthzEntitlements.ServiceDefaults;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Directly exercises the shared CWE-117 barrier reused by every audit/log call site (the sink and
// the three flagged ILogger sites): it must replace CR/LF with a space so no caller-derived value
// can forge a second log line, while leaving clean values — and null/empty — untouched.
public sealed class LogSanitizerTests
{
    [Fact]
    public void Clean_ReplacesCarriageReturn_WithSpace()
    {
        Assert.Equal("a b", LogSanitizer.Clean("a\rb"));
    }

    [Fact]
    public void Clean_ReplacesLineFeed_WithSpace()
    {
        Assert.Equal("a b", LogSanitizer.Clean("a\nb"));
    }

    [Fact]
    public void Clean_ReplacesCrLf_WithTwoSpaces()
    {
        // CR and LF are replaced INDEPENDENTLY (not collapsed), so a Windows newline becomes two
        // spaces — matching the proven LoggingPdpDecisionAuditSink barrier.
        Assert.Equal("a  b", LogSanitizer.Clean("a\r\nb"));
    }

    [Fact]
    public void Clean_NeutralizesForgedSecondLine_InRealisticValue()
    {
        // A caller-controlled id smuggling a CR/LF plus a forged "second" PDP log line.
        var forged = "user-1\r\nPDP decision Permit () provider=reference FORGED";

        var cleaned = LogSanitizer.Clean(forged);

        Assert.NotNull(cleaned);
        Assert.DoesNotContain("\r", cleaned);
        Assert.DoesNotContain("\n", cleaned);
        Assert.Equal("user-1  PDP decision Permit () provider=reference FORGED", cleaned);
    }

    [Fact]
    public void Clean_Null_ReturnsNull()
    {
        Assert.Null(LogSanitizer.Clean(null));
    }

    [Fact]
    public void Clean_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, LogSanitizer.Clean(string.Empty));
    }

    [Fact]
    public void Clean_CleanString_PassesThroughUnchanged()
    {
        // A realistic clean value (an OpenFGA relationship tuple) must be returned byte-for-byte.
        const string clean = "user:carol#can_view@account:acme-checking";
        Assert.Equal(clean, LogSanitizer.Clean(clean));
    }
}
