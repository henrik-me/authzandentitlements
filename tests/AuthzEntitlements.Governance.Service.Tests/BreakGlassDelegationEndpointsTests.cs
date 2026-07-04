using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AuthzEntitlements.Governance.Service.Contracts;
using AuthzEntitlements.Governance.Service.BreakGlass;
using AuthzEntitlements.Governance.Service.Delegation;
using AuthzEntitlements.Governance.Service.Endpoints;
using AuthzEntitlements.Governance.Service.Metering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace AuthzEntitlements.Governance.Service.Tests;

// End-to-end HTTP-surface coverage for the CS21 break-glass + delegation endpoints. The
// Governance test project has no WebApplicationFactory/TestHost package (and none may be added),
// and the existing endpoint tests avoid a live host + Postgres by reading route metadata off a
// WebApplication (GovernanceEndpointAuthorizationTests) or by driving a DefaultHttpContext
// (Bank.Web tests). These grant endpoints are backed by IN-MEMORY singleton stores — no
// DbContext — so we combine both patterns: build a minimal WebApplication that maps ONLY these
// endpoints, then invoke each mapped RouteEndpoint's real RequestDelegate over a DefaultHttpContext.
// This exercises the genuine minimal-API pipeline (JSON model binding, route/query binding, the
// TypedResults status codes) end to end without a server or a database.
public sealed class BreakGlassDelegationEndpointsTests
{
    private const string Tenant = GovernanceTestData.Contoso;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---- Break-glass ----

    [Fact]
    public async Task BreakGlass_Issue_List_Review_RoundTrips()
    {
        using var host = new EndpointHost();

        var issue = await host.InvokeAsync(
            HttpMethods.Post, "/api/governance/break-glass",
            body: new IssueBreakGlassRequest("user-teller1", Tenant, "bank.transaction.create", "core outage", 60));
        Assert.Equal(StatusCodes.Status201Created, issue.Status);
        var created = Deserialize<BreakGlassGrantDto>(issue);
        Assert.True(created.Active);
        Assert.False(created.RequiresReview);
        Assert.Equal("active", created.Status);
        Assert.Null(created.ReviewedAt);

        var list = await host.InvokeAsync(
            HttpMethods.Get, "/api/governance/break-glass", query: ("activeOnly", "true"));
        Assert.Equal(StatusCodes.Status200OK, list.Status);
        Assert.Contains(Deserialize<BreakGlassGrantDto[]>(list), g => g.Id == created.Id);

        var get = await host.InvokeAsync(
            HttpMethods.Get, "/api/governance/break-glass/{id:guid}", routeValue: ("id", created.Id.ToString()));
        Assert.Equal(StatusCodes.Status200OK, get.Status);
        Assert.Equal(created.Id, Deserialize<BreakGlassGrantDto>(get).Id);

        var review = await host.InvokeAsync(
            HttpMethods.Post, "/api/governance/break-glass/{id:guid}/review",
            routeValue: ("id", created.Id.ToString()),
            body: new ReviewBreakGlassRequest("user-compliance1", "approved"));
        Assert.Equal(StatusCodes.Status200OK, review.Status);
        var reviewed = Deserialize<BreakGlassGrantDto>(review);
        Assert.Equal("user-compliance1", reviewed.ReviewedBy);
        Assert.Equal("approved", reviewed.ReviewOutcome);
        Assert.NotNull(reviewed.ReviewedAt);
        Assert.False(reviewed.RequiresReview);
    }

    [Fact]
    public async Task BreakGlass_DoubleReview_Conflicts()
    {
        using var host = new EndpointHost();
        var created = Deserialize<BreakGlassGrantDto>(await host.InvokeAsync(
            HttpMethods.Post, "/api/governance/break-glass",
            body: new IssueBreakGlassRequest("user-teller1", Tenant, "bank.transaction.create", "outage", 60)));

        var first = await host.InvokeAsync(
            HttpMethods.Post, "/api/governance/break-glass/{id:guid}/review",
            routeValue: ("id", created.Id.ToString()),
            body: new ReviewBreakGlassRequest("user-compliance1", "approved"));
        Assert.Equal(StatusCodes.Status200OK, first.Status);

        var second = await host.InvokeAsync(
            HttpMethods.Post, "/api/governance/break-glass/{id:guid}/review",
            routeValue: ("id", created.Id.ToString()),
            body: new ReviewBreakGlassRequest("user-auditor1", "rejected"));
        Assert.Equal(StatusCodes.Status409Conflict, second.Status);
    }

    [Fact]
    public async Task BreakGlass_PendingReview_IsEmpty_ForFreshActiveGrant()
    {
        using var host = new EndpointHost();
        await host.InvokeAsync(
            HttpMethods.Post, "/api/governance/break-glass",
            body: new IssueBreakGlassRequest("user-teller1", Tenant, "bank.transaction.create", "outage", 60));

        var pending = await host.InvokeAsync(HttpMethods.Get, "/api/governance/break-glass/pending-review");

        Assert.Equal(StatusCodes.Status200OK, pending.Status);
        Assert.Empty(Deserialize<BreakGlassGrantDto[]>(pending));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"principalId\":\"\",\"tenantCode\":\"\",\"action\":\"\",\"justification\":\"\",\"durationMinutes\":0}")]
    public async Task BreakGlass_Issue_BlankBody_Is400(string rawBody)
    {
        using var host = new EndpointHost();

        var result = await host.InvokeAsync(HttpMethods.Post, "/api/governance/break-glass", body: rawBody);

        Assert.Equal(StatusCodes.Status400BadRequest, result.Status);
    }

    [Fact]
    public async Task BreakGlass_Issue_NonPositiveDuration_Is400()
    {
        using var host = new EndpointHost();

        var result = await host.InvokeAsync(
            HttpMethods.Post, "/api/governance/break-glass",
            body: new IssueBreakGlassRequest("user-teller1", Tenant, "bank.transaction.create", "outage", 0));

        Assert.Equal(StatusCodes.Status400BadRequest, result.Status);
    }

    [Fact]
    public async Task BreakGlass_Get_UnknownId_Is404()
    {
        using var host = new EndpointHost();

        var result = await host.InvokeAsync(
            HttpMethods.Get, "/api/governance/break-glass/{id:guid}", routeValue: ("id", Guid.NewGuid().ToString()));

        Assert.Equal(StatusCodes.Status404NotFound, result.Status);
    }

    [Fact]
    public async Task BreakGlass_Review_UnknownId_Is404()
    {
        using var host = new EndpointHost();

        var result = await host.InvokeAsync(
            HttpMethods.Post, "/api/governance/break-glass/{id:guid}/review",
            routeValue: ("id", Guid.NewGuid().ToString()),
            body: new ReviewBreakGlassRequest("user-compliance1", "approved"));

        Assert.Equal(StatusCodes.Status404NotFound, result.Status);
    }

    // ---- Delegation ----

    [Fact]
    public async Task Delegation_Create_List_Revoke_RoundTrips()
    {
        using var host = new EndpointHost();

        var create = await host.InvokeAsync(
            HttpMethods.Post, "/api/governance/delegations",
            body: new CreateDelegationRequest(
                "user-manager1", "user-teller1", Tenant,
                ["agent.bank.transaction.read", "agent.bank.transaction.create"], 60));
        Assert.Equal(StatusCodes.Status201Created, create.Status);
        var created = Deserialize<DelegationGrantDto>(create);
        Assert.True(created.Active);
        Assert.Equal("active", created.Status);
        Assert.Equal(["agent.bank.transaction.read", "agent.bank.transaction.create"], created.Scopes);

        var list = await host.InvokeAsync(
            HttpMethods.Get, "/api/governance/delegations", query: ("activeOnly", "true"));
        Assert.Equal(StatusCodes.Status200OK, list.Status);
        Assert.Contains(Deserialize<DelegationGrantDto[]>(list), g => g.Id == created.Id);

        var get = await host.InvokeAsync(
            HttpMethods.Get, "/api/governance/delegations/{id:guid}", routeValue: ("id", created.Id.ToString()));
        Assert.Equal(StatusCodes.Status200OK, get.Status);

        var revoke = await host.InvokeAsync(
            HttpMethods.Post, "/api/governance/delegations/{id:guid}/revoke",
            routeValue: ("id", created.Id.ToString()),
            body: new RevokeDelegationRequest("user-manager1"));
        Assert.Equal(StatusCodes.Status200OK, revoke.Status);
        var revoked = Deserialize<DelegationGrantDto>(revoke);
        Assert.False(revoked.Active);
        Assert.Equal("revoked", revoked.Status);
        Assert.Equal("user-manager1", revoked.RevokedBy);

        var afterList = await host.InvokeAsync(
            HttpMethods.Get, "/api/governance/delegations", query: ("activeOnly", "true"));
        Assert.DoesNotContain(Deserialize<DelegationGrantDto[]>(afterList), g => g.Id == created.Id);
    }

    [Fact]
    public async Task Delegation_DoubleRevoke_Conflicts()
    {
        using var host = new EndpointHost();
        var created = Deserialize<DelegationGrantDto>(await host.InvokeAsync(
            HttpMethods.Post, "/api/governance/delegations",
            body: new CreateDelegationRequest("user-manager1", "user-teller1", Tenant, ["agent.bank.x"], 60)));

        var first = await host.InvokeAsync(
            HttpMethods.Post, "/api/governance/delegations/{id:guid}/revoke",
            routeValue: ("id", created.Id.ToString()), body: new RevokeDelegationRequest("user-manager1"));
        Assert.Equal(StatusCodes.Status200OK, first.Status);

        var second = await host.InvokeAsync(
            HttpMethods.Post, "/api/governance/delegations/{id:guid}/revoke",
            routeValue: ("id", created.Id.ToString()), body: new RevokeDelegationRequest("user-manager1"));
        Assert.Equal(StatusCodes.Status409Conflict, second.Status);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"managerId\":\"m\",\"delegateId\":\"d\",\"tenantCode\":\"CONTOSO\",\"scopes\":[],\"durationMinutes\":60}")]
    public async Task Delegation_Create_BlankBody_Is400(string rawBody)
    {
        using var host = new EndpointHost();

        var result = await host.InvokeAsync(HttpMethods.Post, "/api/governance/delegations", body: rawBody);

        Assert.Equal(StatusCodes.Status400BadRequest, result.Status);
    }

    [Fact]
    public async Task Delegation_Create_SelfDelegation_Is400()
    {
        using var host = new EndpointHost();

        var result = await host.InvokeAsync(
            HttpMethods.Post, "/api/governance/delegations",
            body: new CreateDelegationRequest("user-manager1", "user-manager1", Tenant, ["agent.bank.x"], 60));

        Assert.Equal(StatusCodes.Status400BadRequest, result.Status);
    }

    [Fact]
    public async Task Delegation_Revoke_UnknownId_Is404()
    {
        using var host = new EndpointHost();

        var result = await host.InvokeAsync(
            HttpMethods.Post, "/api/governance/delegations/{id:guid}/revoke",
            routeValue: ("id", Guid.NewGuid().ToString()), body: new RevokeDelegationRequest("user-manager1"));

        Assert.Equal(StatusCodes.Status404NotFound, result.Status);
    }

    private static T Deserialize<T>(Response response) =>
        JsonSerializer.Deserialize<T>(response.Body, Json)!;

    private sealed record Response(int Status, string Body);

    // Builds a minimal WebApplication that maps ONLY the CS21 endpoints over the in-memory
    // singleton stores (no DbContext, no Keycloak, no PDP client — so no Postgres), then invokes
    // a mapped RouteEndpoint's real RequestDelegate against a DefaultHttpContext.
    private sealed class EndpointHost : IDisposable
    {
        private readonly WebApplication _app;
        private readonly IReadOnlyList<RouteEndpoint> _endpoints;

        public EndpointHost()
        {
            var builder = WebApplication.CreateBuilder();
            builder.Services.AddSingleton<BreakGlassGrantStore>();
            builder.Services.AddSingleton<DelegationGrantStore>();
            builder.Services.AddSingleton<GovernanceMetrics>();
            builder.Services.AddSingleton<IGovernanceAuditSink, LoggingGovernanceAuditSink>();
            builder.Services.ConfigureHttpJsonOptions(options =>
                options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

            _app = builder.Build();
            _app.MapBreakGlassDelegationEndpoints();

            _endpoints = ((IEndpointRouteBuilder)_app).DataSources
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .ToList();
        }

        // Drives the mapped endpoint whose raw route template + HTTP method match. body may be a
        // typed request record (serialized as JSON) or a raw JSON string (to test malformed/blank
        // payloads verbatim). routeValue supplies the {id} segment; query supplies a filter.
        public async Task<Response> InvokeAsync(
            string method,
            string template,
            object? body = null,
            (string Key, string Value)? routeValue = null,
            (string Key, string Value)? query = null)
        {
            var endpoint = _endpoints.Single(e =>
                string.Equals(e.RoutePattern.RawText, template, StringComparison.Ordinal)
                && (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(method) ?? false));

            var context = new DefaultHttpContext { RequestServices = _app.Services };
            context.Request.Method = method;
            context.SetEndpoint(endpoint);

            if (routeValue is { } rv)
            {
                context.Request.RouteValues[rv.Key] = rv.Value;
            }

            if (query is { } q)
            {
                context.Request.Query = new QueryCollection(new Dictionary<string, StringValues>
                {
                    [q.Key] = q.Value,
                });
            }

            if (body is not null)
            {
                var json = body as string ?? JsonSerializer.Serialize(body, Json);
                var bytes = Encoding.UTF8.GetBytes(json);
                context.Request.Body = new MemoryStream(bytes);
                context.Request.ContentLength = bytes.Length;
                context.Request.ContentType = "application/json";

                // A hand-built DefaultHttpContext carries no IHttpRequestBodyDetectionFeature, and
                // the minimal-API JSON body reader treats that as "cannot have a body" and binds
                // null (which would trip every handler's fail-closed 400). Advertise a body so the
                // real deserialization path runs.
                context.Features.Set<IHttpRequestBodyDetectionFeature>(new CanHaveBodyFeature());
            }

            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await endpoint.RequestDelegate!(context);

            responseBody.Position = 0;
            using var reader = new StreamReader(responseBody, Encoding.UTF8);
            var text = await reader.ReadToEndAsync();
            return new Response(context.Response.StatusCode, text);
        }

        public void Dispose() => ((IDisposable)_app).Dispose();

        private sealed class CanHaveBodyFeature : IHttpRequestBodyDetectionFeature
        {
            public bool CanHaveBody => true;
        }
    }
}
