using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// CS34 (CWE-117): the OpenFGA fail-closed catch logs the checked (user, relation, object), and the
// user/object embed caller-controlled ids from the anonymous /evaluate body. A value carrying CR/LF
// must NOT forge a second log line — the rendered warning must stay on one line.
public sealed class OpenFgaProviderLogForgingTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }

    [Fact]
    public void Evaluate_WhenSeamThrows_StripsNewlines_FromFailClosedWarning()
    {
        var logger = new CapturingLogger<OpenFgaProvider>();
        var provider = new OpenFgaProvider(
            FakeOpenFgaCheckClient.Throwing(new InvalidOperationException("engine down")),
            logger);

        // The subject id (caller-controlled) smuggles a CR/LF plus a forged second warning line; the
        // mapper qualifies it into check.User, which the fail-closed catch renders into the log.
        var request = new AccessRequest(
            new Subject("user", "carol\r\nOpenFGA Check failed for user=evil relation=owner FORGED", []),
            new ActionRequest(ActionNames.AccountRead),
            new Resource("account", Id: "personal-carol"),
            new EvaluationContext([]));

        provider.Evaluate(request);

        var message = Assert.Single(logger.Messages);
        Assert.DoesNotContain("\n", message);
        Assert.DoesNotContain("\r", message);
        Assert.Contains("carol  OpenFGA Check failed for user=evil relation=owner FORGED", message);
    }
}
