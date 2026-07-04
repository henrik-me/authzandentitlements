using System.Net;
using System.Text.Json;
using AuthzEntitlements.Bank.Web.Clients;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

public class GovernanceClientTests
{
    private static HttpClient Client(StubHttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://governance-service") };

    [Fact]
    public async Task GetAccessPackagesAsync_maps_list()
    {
        const string json = """
        [
          {
            "code": "pkg-approver",
            "displayName": "Approver",
            "description": "Can approve",
            "defaultDurationMinutes": 60,
            "requiresApproval": true,
            "roles": ["BranchManager"]
          }
        ]
        """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, json);
        var client = new GovernanceClient(Client(handler));

        var packages = await client.GetAccessPackagesAsync();

        Assert.Equal("/api/governance/access-packages", handler.LastRequest!.RequestUri!.AbsolutePath);
        var package = Assert.Single(packages);
        Assert.Equal("pkg-approver", package.Code);
        Assert.True(package.RequiresApproval);
    }

    [Fact]
    public async Task CreateRequestAsync_posts_body_and_maps_response()
    {
        const string json = """
        {
          "id": "aaaaaaaa-0000-0000-0000-000000000001",
          "principalId": "user-teller1",
          "tenantCode": "CONTOSO",
          "accessPackageCode": "pkg-approver",
          "justification": "cover",
          "requestedDurationMinutes": 60,
          "status": "Pending",
          "sodOutcome": "Clear",
          "sodReason": null,
          "requestedAt": "2026-01-02T09:00:00+00:00",
          "decidedBy": null,
          "decidedAt": null
        }
        """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.Created, json);
        var client = new GovernanceClient(Client(handler));
        var body = new CreateAccessRequestBody("user-teller1", "pkg-approver", "cover", 60);

        var result = await client.CreateRequestAsync(body);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/governance/requests", handler.LastRequest!.RequestUri!.AbsolutePath);
        using var sent = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal("user-teller1", sent.RootElement.GetProperty("principalId").GetString());
        Assert.True(result.IsSuccess);
        Assert.Equal("Pending", result.Value!.Status);
    }

    [Fact]
    public async Task ApproveRequestAsync_maps_sod_conflict_as_failure()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Conflict, "SoD conflict");
        var client = new GovernanceClient(Client(handler));
        var id = new Guid("aaaaaaaa-0000-0000-0000-000000000001");

        var result = await client.ApproveRequestAsync(id, new ApproveRequestBody("user-manager1"));

        Assert.Equal($"/api/governance/requests/{id}/approve", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("SoD conflict", result.Error);
    }

    [Fact]
    public async Task GetPrincipalAccessAsync_requests_path_and_maps()
    {
        const string json = """
        {
          "principalId": "user-teller1",
          "tenantCode": "CONTOSO",
          "effectiveRoles": ["Teller", "BranchManager"],
          "baselineRoles": ["Teller"],
          "activeGrantPackages": ["pkg-approver"]
        }
        """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, json);
        var client = new GovernanceClient(Client(handler));

        var access = await client.GetPrincipalAccessAsync("user-teller1");

        Assert.Equal("/api/governance/principals/user-teller1/access", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.NotNull(access);
        Assert.Equal(2, access!.EffectiveRoles.Length);
    }
}
