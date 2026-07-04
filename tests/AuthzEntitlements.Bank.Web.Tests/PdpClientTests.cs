using System.Net;
using System.Text.Json;
using AuthzEntitlements.Bank.Web.Clients;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

public class PdpClientTests
{
    private static HttpClient Client(StubHttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://authz-pdp") };

    [Fact]
    public async Task EvaluateAsync_posts_native_request_and_maps_permit()
    {
        const string json = """
        {
          "decision": "Permit",
          "reasons": [ { "code": "Permit", "message": "allowed" } ],
          "obligations": [ { "id": "require_approval" } ]
        }
        """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, json);
        var client = new PdpClient(Client(handler));
        var request = new PdpAccessRequestDto(
            new PdpSubjectDto("user", "40000000-0000-0000-0000-000000000001", ["Teller"], "CONTOSO", "NM01"),
            new PdpActionDto("bank.transaction.create"),
            new PdpResourceDto("transaction", Amount: 15000m, Tenant: "CONTOSO"),
            new PdpContextDto(["bank.transactions.write"]));

        var decision = await client.EvaluateAsync(request);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/authz/evaluate", handler.LastRequest!.RequestUri!.AbsolutePath);

        using var sent = JsonDocument.Parse(handler.LastBody!);
        var root = sent.RootElement;
        Assert.Equal("user", root.GetProperty("subject").GetProperty("type").GetString());
        Assert.Equal("bank.transaction.create", root.GetProperty("action").GetProperty("name").GetString());
        Assert.Equal(15000m, root.GetProperty("resource").GetProperty("amount").GetDecimal());

        Assert.NotNull(decision);
        Assert.Equal("Permit", decision!.Decision);
        Assert.Equal("require_approval", Assert.Single(decision.Obligations!).Id);
    }

    [Fact]
    public async Task EvaluateAsync_fails_closed_to_null_on_bad_request()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.BadRequest, "malformed");
        var client = new PdpClient(Client(handler));
        var request = new PdpAccessRequestDto(
            new PdpSubjectDto("user", "x", []),
            new PdpActionDto("bank.read"),
            new PdpResourceDto("account"),
            new PdpContextDto([]));

        var decision = await client.EvaluateAsync(request);

        Assert.Null(decision);
    }
}
