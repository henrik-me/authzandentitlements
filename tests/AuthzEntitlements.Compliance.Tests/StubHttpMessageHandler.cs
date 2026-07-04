namespace AuthzEntitlements.Compliance.Tests;

// A minimal HttpMessageHandler stub for exercising HttpGovernanceClient without a network. It
// either returns a configured (status, body) response or surfaces a configured transport-level
// exception as a faulted task — the two cases HttpGovernanceClient must classify differently
// (reached-but-error → fail closed; transport failure → offline self-skip).
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(System.Net.HttpStatusCode status, string body)
        : this(_ => new HttpResponseMessage(status) { Content = new StringContent(body) })
    {
    }

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    // A handler that fails at the transport level (connection refused / DNS / timeout).
    public static StubHttpMessageHandler Throwing(Exception exception) =>
        new(_ => throw exception);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Honor cancellation like a real handler would, so a pre-cancelled token surfaces as a
        // cancelled task (never a synchronously-returned success).
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<HttpResponseMessage>(cancellationToken);
        }

        try
        {
            return Task.FromResult(_responder(request));
        }
        catch (Exception ex)
        {
            return Task.FromException<HttpResponseMessage>(ex);
        }
    }
}
