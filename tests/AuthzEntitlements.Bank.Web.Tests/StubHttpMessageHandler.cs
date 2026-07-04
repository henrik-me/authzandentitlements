using System.Net;
using System.Text;

namespace AuthzEntitlements.Bank.Web.Tests;

// Records the last request (method, URI, body) and returns a canned response, so a typed
// client can be exercised offline with no server, Docker, or Keycloak.
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        _responder = responder;

    public StubHttpMessageHandler(HttpStatusCode status, string json)
        : this(_ => JsonResponse(status, json))
    {
    }

    public HttpRequestMessage? LastRequest { get; private set; }

    public string? LastBody { get; private set; }

    public int CallCount { get; private set; }

    public static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        LastRequest = request;
        LastBody = request.Content is not null
            ? await request.Content.ReadAsStringAsync(cancellationToken)
            : null;

        return _responder(request);
    }
}
