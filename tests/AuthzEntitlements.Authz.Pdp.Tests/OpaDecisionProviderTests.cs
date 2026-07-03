using System.Net;
using System.Text;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers.Adapters.Opa;
using Microsoft.Extensions.Options;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Deterministic coverage of the OPA adapter with NO live OPA: a fake HttpMessageHandler returns
// canned responses (and captures the outgoing request) behind a real HttpClient wired through a
// tiny in-test IHttpClientFactory. Covers request shaping (path + camelCase {"input":{...}} body),
// response mapping (Permit + obligations, each Deny reason), and the fail-closed posture.
public sealed class OpaDecisionProviderTests
{
    private const string DecisionPath = "v1/data/authz/bank/decision";
    private const string Contoso = "CONTOSO";

    // --- Test doubles -------------------------------------------------------

    // Records the last request (and its serialized body) and returns whatever the responder
    // produces. Overrides the synchronous Send because the provider calls HttpClient.Send.
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
            _responder = responder;

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override HttpResponseMessage Send(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                using var reader = new StreamReader(request.Content.ReadAsStream(cancellationToken));
                LastBody = reader.ReadToEnd();
            }

            return _responder(request);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) =>
            new(_handler, disposeHandler: false)
            {
                BaseAddress = new Uri("http://localhost:8181"),
            };
    }

    // Simulates the named HttpClient's configure delegate throwing when CreateClient runs — the
    // shape a malformed Opa:BaseUrl (UriFormatException) or invalid Opa:TimeoutSeconds
    // (ArgumentOutOfRangeException) takes at request time.
    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        private readonly Exception _toThrow;

        public ThrowingHttpClientFactory(Exception toThrow) => _toThrow = toThrow;

        public HttpClient CreateClient(string name) => throw _toThrow;
    }

    // --- Fixtures -----------------------------------------------------------

    private static OpaDecisionProvider ProviderWith(StubHandler handler) =>
        new(new StubHttpClientFactory(handler), Options.Create(new OpaOptions()));

    private static OpaDecisionProvider ProviderReturning(string json, out StubHandler handler)
    {
        handler = new StubHandler(_ => Ok(json));
        return ProviderWith(handler);
    }

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private static AccessRequest TransactionCreate() =>
        new(
            new Subject("user", "maker", ["Teller"], Contoso),
            new ActionRequest(ActionNames.TransactionCreate),
            new Resource("transaction", Tenant: Contoso, Amount: 15_000m, MakerId: "maker"),
            new EvaluationContext([ScopeNames.TransactionsWrite]));

    // --- Name ---------------------------------------------------------------

    [Fact]
    public void Name_IsOpa()
    {
        var provider = ProviderReturning(
            """{"result":{"decision":"Deny","reason":"MissingScope"}}""", out _);

        Assert.Equal("opa", provider.Name);
    }

    // --- Request shaping ----------------------------------------------------

    [Fact]
    public void Evaluate_PostsToDecisionPath()
    {
        var provider = ProviderReturning(
            """{"result":{"decision":"Deny","reason":"MissingScope"}}""", out var handler);

        provider.Evaluate(TransactionCreate());

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.EndsWith(DecisionPath, handler.LastRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public void Evaluate_WrapsRequestUnderInput()
    {
        var provider = ProviderReturning(
            """{"result":{"decision":"Deny","reason":"MissingScope"}}""", out var handler);

        provider.Evaluate(TransactionCreate());

        Assert.NotNull(handler.LastBody);
        Assert.Contains("\"input\"", handler.LastBody!);
    }

    [Fact]
    public void Evaluate_SerializesBodyInCamelCase()
    {
        var provider = ProviderReturning(
            """{"result":{"decision":"Deny","reason":"MissingScope"}}""", out var handler);

        provider.Evaluate(TransactionCreate());

        var body = handler.LastBody!;
        // action.name, resource.makerId, and context.scopes must be present in camelCase.
        Assert.Contains("\"action\":{\"name\":\"bank.transaction.create\"}", body);
        Assert.Contains("\"makerId\":\"maker\"", body);
        Assert.Contains("\"scopes\":[\"bank.transactions.write\"]", body);
    }

    // --- Response mapping: Permit + obligations -----------------------------

    [Fact]
    public void Permit_WithRequireApproval_MapsObligation()
    {
        var provider = ProviderReturning(
            """{"result":{"decision":"Permit","reason":"Permit","obligations":["require_approval"]}}""",
            out _);

        var decision = provider.Evaluate(TransactionCreate());

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Equal(ReasonCodes.Permit, decision.Reasons[0].Code);
        var obligation = Assert.Single(decision.Obligations);
        Assert.Equal(ObligationIds.RequireApproval, obligation.Id);
    }

    [Fact]
    public void Permit_WithPostImmediately_MapsObligation()
    {
        var provider = ProviderReturning(
            """{"result":{"decision":"Permit","reason":"Permit","obligations":["post_immediately"]}}""",
            out _);

        var decision = provider.Evaluate(TransactionCreate());

        Assert.Equal(Decision.Permit, decision.Decision);
        var obligation = Assert.Single(decision.Obligations);
        Assert.Equal(ObligationIds.PostImmediately, obligation.Id);
    }

    [Fact]
    public void Permit_WithEmptyObligations_HasNone()
    {
        var provider = ProviderReturning(
            """{"result":{"decision":"Permit","reason":"Permit","obligations":[]}}""", out _);

        var decision = provider.Evaluate(TransactionCreate());

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Empty(decision.Obligations);
    }

    [Fact]
    public void Permit_WithNoObligationsField_HasNone()
    {
        var provider = ProviderReturning(
            """{"result":{"decision":"Permit","reason":"Permit"}}""", out _);

        var decision = provider.Evaluate(TransactionCreate());

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Empty(decision.Obligations);
    }

    [Fact]
    public void Permit_DropsUnknownObligationStrings()
    {
        var provider = ProviderReturning(
            """{"result":{"decision":"Permit","reason":"Permit","obligations":["mystery"]}}""",
            out _);

        var decision = provider.Evaluate(TransactionCreate());

        Assert.Equal(Decision.Permit, decision.Decision);
        Assert.Empty(decision.Obligations);
    }

    // --- Response mapping: Deny reasons -------------------------------------

    [Theory]
    [InlineData("MissingScope")]
    [InlineData("TenantMismatch")]
    [InlineData("RoleNotAuthorized")]
    [InlineData("SubjectNotMaker")]
    [InlineData("MakerEqualsChecker")]
    [InlineData("NotPending")]
    [InlineData("UnknownAction")]
    public void Deny_SurfacesReasonCode(string reasonCode)
    {
        var provider = ProviderReturning(
            "{\"result\":{\"decision\":\"Deny\",\"reason\":\"" + reasonCode + "\"}}", out _);

        var decision = provider.Evaluate(TransactionCreate());

        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal(reasonCode, decision.Reasons[0].Code);
        Assert.Empty(decision.Obligations);
    }

    // --- Fail closed --------------------------------------------------------

    [Fact]
    public void FailClosed_WhenTransportThrows()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var provider = ProviderWith(handler);

        AssertProviderUnavailable(provider.Evaluate(TransactionCreate()));
    }

    [Fact]
    public void FailClosed_WhenTimeout()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timed out"));
        var provider = ProviderWith(handler);

        AssertProviderUnavailable(provider.Evaluate(TransactionCreate()));
    }

    [Fact]
    public void FailClosed_OnNonSuccessStatus()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var provider = ProviderWith(handler);

        AssertProviderUnavailable(provider.Evaluate(TransactionCreate()));
    }

    [Fact]
    public void FailClosed_OnEmptyBody()
    {
        // OPA returns "{}" (no "result") when the policy is undefined for the input.
        var provider = ProviderReturning("{}", out _);

        AssertProviderUnavailable(provider.Evaluate(TransactionCreate()));
    }

    [Fact]
    public void FailClosed_OnUnknownDecisionString()
    {
        var provider = ProviderReturning(
            """{"result":{"decision":"Maybe","reason":"Permit"}}""", out _);

        AssertProviderUnavailable(provider.Evaluate(TransactionCreate()));
    }

    [Fact]
    public void FailClosed_OnMissingReason()
    {
        var provider = ProviderReturning(
            """{"result":{"decision":"Permit"}}""", out _);

        AssertProviderUnavailable(provider.Evaluate(TransactionCreate()));
    }

    [Fact]
    public void FailClosed_OnUnparseableBody()
    {
        var provider = ProviderReturning("this is not json", out _);

        AssertProviderUnavailable(provider.Evaluate(TransactionCreate()));
    }

    [Fact]
    public void FailClosed_WhenClientConstructionThrows()
    {
        // A malformed Opa:BaseUrl / invalid Opa:TimeoutSeconds surfaces as a throw from the named
        // HttpClient's configure delegate when CreateClient runs. The adapter must still fail closed
        // rather than let the exception escape as a 500.
        var provider = new OpaDecisionProvider(
            new ThrowingHttpClientFactory(new UriFormatException("bad base url")),
            Options.Create(new OpaOptions()));

        AssertProviderUnavailable(provider.Evaluate(TransactionCreate()));
    }

    private static void AssertProviderUnavailable(AccessDecision decision)
    {
        Assert.Equal(Decision.Deny, decision.Decision);
        Assert.Equal("ProviderUnavailable", decision.Reasons[0].Code);
        Assert.Empty(decision.Obligations);
    }
}
