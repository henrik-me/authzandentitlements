using System.Net;
using System.Text;
using System.Text.Json;
using AuthzEntitlements.Governance.Service.Sod;
using Xunit;

namespace AuthzEntitlements.Governance.Service.Tests;

// The PDP SoD client must map the AuthZEN decision correctly and, above all, fail closed:
// any transport fault, non-success status, or missing/malformed body becomes Unavailable
// (never a permit, never a thrown exception) so the approval workflow denies safely
// (item d). Only genuine caller cancellation propagates.
public sealed class PdpSodClientTests
{
    private static readonly string[] ProposedRoles = ["BranchManager", "ComplianceOfficer"];

    [Fact]
    public async Task Evaluate_Permit_MapsToPermit()
    {
        var client = ClientWith(_ => Json(HttpStatusCode.OK,
            """{"decision":"Permit","reasons":[],"obligations":[]}"""));

        var result = await Evaluate(client);

        Assert.True(result.IsPermit);
    }

    [Fact]
    public async Task Evaluate_Deny_MapsToDenyWithReasonCode()
    {
        var client = ClientWith(_ => Json(HttpStatusCode.OK,
            """{"decision":"Deny","reasons":[{"code":"SodConflict","message":"BranchManager conflicts with Auditor"}],"obligations":[]}"""));

        var result = await Evaluate(client);

        Assert.True(result.IsDeny);
        Assert.Equal("SodConflict", result.ReasonCode);
        Assert.Equal("BranchManager conflicts with Auditor", result.ReasonMessage);
    }

    [Fact]
    public async Task Evaluate_DenyProviderUnavailable_FailsClosed()
    {
        // The PDP's own OPA engine being down surfaces as Deny/ProviderUnavailable. That is
        // an infrastructure outage, not an SoD business denial, so it must fail closed
        // (Unavailable -> 503, request stays Pending/retryable) — never a permanent reject.
        var client = ClientWith(_ => Json(HttpStatusCode.OK,
            """{"decision":"Deny","reasons":[{"code":"ProviderUnavailable","message":"opa unreachable"}],"obligations":[]}"""));

        var result = await Evaluate(client);

        Assert.True(result.IsUnavailable);
        Assert.False(result.IsDeny);
    }

    [Fact]
    public async Task Evaluate_DenyNonSodReason_FailsClosed()
    {
        // Any Deny whose primary reason is not exactly "SodConflict" (here UnknownAction) is
        // not a business SoD denial; fail closed rather than permanently rejecting.
        var client = ClientWith(_ => Json(HttpStatusCode.OK,
            """{"decision":"Deny","reasons":[{"code":"UnknownAction","message":"unknown action"}],"obligations":[]}"""));

        var result = await Evaluate(client);

        Assert.True(result.IsUnavailable);
        Assert.False(result.IsDeny);
    }

    [Fact]
    public async Task Evaluate_NonSuccessStatus_FailsClosed()
    {
        var client = ClientWith(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var result = await Evaluate(client);

        Assert.True(result.IsUnavailable);
        Assert.False(result.IsPermit);
    }

    [Fact]
    public async Task Evaluate_TransportException_FailsClosed()
    {
        var client = ClientWith(_ => throw new HttpRequestException("connection refused"));

        var result = await Evaluate(client);

        Assert.True(result.IsUnavailable);
    }

    [Fact]
    public async Task Evaluate_MalformedBody_FailsClosed()
    {
        var client = ClientWith(_ => Json(HttpStatusCode.OK, "not-json{"));

        var result = await Evaluate(client);

        Assert.True(result.IsUnavailable);
    }

    [Fact]
    public async Task Evaluate_EmptyDecision_FailsClosed()
    {
        var client = ClientWith(_ => Json(HttpStatusCode.OK,
            """{"decision":null,"reasons":[],"obligations":[]}"""));

        var result = await Evaluate(client);

        Assert.True(result.IsUnavailable);
    }

    [Fact]
    public async Task Evaluate_DenyWithoutReason_FailsClosed()
    {
        // A deny with no reason is ambiguous; fail closed rather than inventing a code.
        var client = ClientWith(_ => Json(HttpStatusCode.OK,
            """{"decision":"Deny","reasons":[],"obligations":[]}"""));

        var result = await Evaluate(client);

        Assert.True(result.IsUnavailable);
    }

    [Fact]
    public async Task Evaluate_UnknownDecision_FailsClosed()
    {
        var client = ClientWith(_ => Json(HttpStatusCode.OK,
            """{"decision":"Maybe","reasons":[],"obligations":[]}"""));

        var result = await Evaluate(client);

        Assert.True(result.IsUnavailable);
    }

    [Fact]
    public async Task Evaluate_PostsAuthZenContractShape()
    {
        var handler = new RecordingHandler(_ => Json(HttpStatusCode.OK,
            """{"decision":"Permit","reasons":[],"obligations":[]}"""));
        var client = new PdpSodClient(new HttpClient(handler) { BaseAddress = new Uri("http://authz-pdp") });

        await client.EvaluateAsync("user-teller1", "CONTOSO", ProposedRoles, "branch-approver", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.Equal("/api/authz/evaluate", handler.LastPath);

        var body = handler.LastBody;
        Assert.NotNull(body);
        using var doc = JsonDocument.Parse(body!);
        var root = doc.RootElement;

        var subject = root.GetProperty("subject");
        Assert.Equal("user", subject.GetProperty("type").GetString());
        Assert.Equal("user-teller1", subject.GetProperty("id").GetString());
        Assert.Equal("CONTOSO", subject.GetProperty("tenant").GetString());
        Assert.Equal(
            ["BranchManager", "ComplianceOfficer"],
            subject.GetProperty("roles").EnumerateArray().Select(e => e.GetString() ?? string.Empty));

        Assert.Equal(PdpSodClient.ActionName, root.GetProperty("action").GetProperty("name").GetString());

        var resource = root.GetProperty("resource");
        Assert.Equal("access-grant", resource.GetProperty("type").GetString());
        Assert.Equal("branch-approver", resource.GetProperty("id").GetString());
        Assert.Empty(root.GetProperty("context").GetProperty("scopes").EnumerateArray());
    }

    [Fact]
    public async Task Evaluate_CancellationNotRequestedByCaller_FailsClosed()
    {
        // A timeout surfaces as TaskCanceledException while the caller's token is not
        // cancelled; that is a transport fault and must fail closed, not propagate.
        var client = ClientWith(_ => throw new TaskCanceledException("timed out"));

        var result = await client.EvaluateAsync(
            "user-teller1", "CONTOSO", ProposedRoles, "branch-approver", CancellationToken.None);

        Assert.True(result.IsUnavailable);
    }

    [Fact]
    public async Task Evaluate_CallerCancelled_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var client = ClientWith(_ => throw new OperationCanceledException(cts.Token));

        // Genuine caller cancellation must not be swallowed as "unavailable".
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.EvaluateAsync(
            "user-teller1", "CONTOSO", ProposedRoles, "branch-approver", cts.Token));
    }

    private static Task<SodCheckResult> Evaluate(PdpSodClient client) =>
        client.EvaluateAsync("user-teller1", "CONTOSO", ProposedRoles, "branch-approver", CancellationToken.None);

    private static PdpSodClient ClientWith(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new HttpClient(new RecordingHandler(responder)) { BaseAddress = new Uri("http://authz-pdp") });

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // Records the outgoing request (method/path/body) then delegates to the responder, so a
    // test can both assert the wire shape and drive the fail-closed paths.
    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpMethod? LastMethod { get; private set; }
        public string? LastPath { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastPath = request.RequestUri?.AbsolutePath;
            if (request.Content is not null)
            {
                LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            return responder(request);
        }
    }
}
