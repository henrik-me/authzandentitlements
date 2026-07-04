using System.Net;
using System.Text.Json;
using AuthzEntitlements.Bank.Web.Clients;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

public class PlaygroundAndAuditClientTests
{
    private static HttpClient Pdp(StubHttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://authz-pdp") };

    private static HttpClient AuditHttp(StubHttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://audit-service") };

    private static PlaygroundFanoutRequestDto SampleFanout(IReadOnlyList<string>? engines) =>
        new(
            new PdpAccessRequestDto(
                new PdpSubjectDto("user", "user-teller1", ["Teller"], "CONTOSO"),
                new PdpActionDto("bank.transaction.create"),
                new PdpResourceDto("transaction", Amount: 15000m, Tenant: "CONTOSO", MakerId: "user-teller1"),
                new PdpContextDto(["bank.transactions.write"])),
            engines);

    // ---- PdpClient.FanoutAsync ----

    [Fact]
    public async Task FanoutAsync_posts_request_and_engines_and_parses_multi_engine_response()
    {
        const string json = """
        {
          "results": [
            {
              "engine": "reference",
              "decision": "Permit",
              "reasons": [ { "code": "Permit", "message": "allowed" } ],
              "obligations": [ { "id": "require_approval" } ],
              "explanation": {
                "engine": "reference",
                "determiningRule": "all-rules-satisfied",
                "policyReferences": [ { "kind": "rule", "reference": "maker-checker", "detail": null } ],
                "narrative": "All rules satisfied."
              },
              "latencyMs": 0.42,
              "traceId": "abc123",
              "available": true,
              "unavailableReason": null
            },
            {
              "engine": "cedar",
              "decision": "Permit",
              "reasons": [ { "code": "Permit", "message": "allowed" } ],
              "obligations": [],
              "explanation": null,
              "latencyMs": 1.10,
              "traceId": "abc123",
              "available": true,
              "unavailableReason": null
            }
          ],
          "traceId": "abc123",
          "allAgree": true
        }
        """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, json);
        var client = new PdpClient(Pdp(handler));

        var response = await client.FanoutAsync(SampleFanout(["reference", "cedar"]));

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/authz/playground/fanout", handler.LastRequest!.RequestUri!.AbsolutePath);

        using var sent = JsonDocument.Parse(handler.LastBody!);
        var root = sent.RootElement;
        Assert.Equal("user-teller1", root.GetProperty("request").GetProperty("subject").GetProperty("id").GetString());
        var engines = root.GetProperty("engines").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Equal(new[] { "reference", "cedar" }, engines);

        Assert.NotNull(response);
        Assert.Equal(2, response!.Results.Count);
        Assert.True(response.AllAgree);
        var reference = response.Results[0];
        Assert.Equal("reference", reference.Engine);
        Assert.Equal("Permit", reference.Decision);
        Assert.Equal("all-rules-satisfied", reference.Explanation!.DeterminingRule);
        Assert.Equal(0.42, reference.LatencyMs);
        Assert.True(reference.Available);
        Assert.Equal("require_approval", Assert.Single(reference.Obligations!).Id);
    }

    [Fact]
    public async Task FanoutAsync_null_engines_serializes_to_null()
    {
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK, """{ "results": [], "traceId": null, "allAgree": true }""");
        var client = new PdpClient(Pdp(handler));

        _ = await client.FanoutAsync(SampleFanout(null));

        using var sent = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(JsonValueKind.Null, sent.RootElement.GetProperty("engines").ValueKind);
    }

    [Fact]
    public async Task FanoutAsync_parses_unavailable_engine_row()
    {
        const string json = """
        {
          "results": [
            {
              "engine": "openfga",
              "decision": "Deny",
              "reasons": [],
              "obligations": [],
              "explanation": null,
              "latencyMs": 0.0,
              "traceId": null,
              "available": false,
              "unavailableReason": "connection refused"
            }
          ],
          "traceId": null,
          "allAgree": true
        }
        """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, json);
        var client = new PdpClient(Pdp(handler));

        var response = await client.FanoutAsync(SampleFanout(["openfga"]));

        var row = Assert.Single(response!.Results);
        Assert.False(row.Available);
        Assert.Equal("connection refused", row.UnavailableReason);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task FanoutAsync_fails_closed_to_null_on_non_success(HttpStatusCode status)
    {
        var handler = new StubHttpMessageHandler(status, "problem");
        var client = new PdpClient(Pdp(handler));

        Assert.Null(await client.FanoutAsync(SampleFanout(null)));
    }

    [Fact]
    public async Task FanoutAsync_fails_closed_to_null_on_malformed_json()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "{ not json ]");
        var client = new PdpClient(Pdp(handler));

        Assert.Null(await client.FanoutAsync(SampleFanout(null)));
    }

    [Fact]
    public async Task FanoutAsync_fails_closed_to_null_on_transport_error()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("boom"));
        var client = new PdpClient(Pdp(handler));

        Assert.Null(await client.FanoutAsync(SampleFanout(null)));
    }

    // ---- AuditClient.GetEntriesAsync ----

    [Fact]
    public async Task GetEntriesAsync_builds_query_string_from_non_null_fields()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = new AuditClient(AuditHttp(handler));

        await client.GetEntriesAsync(new AuditQuery(
            Sequence: 7, Subject: "user-teller1", Decision: "Permit", Limit: 25));

        var uri = handler.LastRequest!.RequestUri!;
        Assert.Equal("/api/audit/entries", uri.AbsolutePath);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("7", query["sequence"].ToString());
        Assert.Equal("user-teller1", query["subject"].ToString());
        Assert.Equal("Permit", query["decision"].ToString());
        Assert.Equal("25", query["limit"].ToString());
        Assert.False(query.ContainsKey("tenant"));
    }

    [Fact]
    public async Task GetEntriesAsync_url_encodes_filter_values()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = new AuditClient(AuditHttp(handler));

        await client.GetEntriesAsync(new AuditQuery(Action: "bank.account.read", Trace: "a b&c"));

        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(
            handler.LastRequest!.RequestUri!.Query);
        Assert.Equal("bank.account.read", query["action"].ToString());
        Assert.Equal("a b&c", query["trace"].ToString());
    }

    [Fact]
    public async Task GetEntriesAsync_parses_entry_list()
    {
        const string json = """
        [
          {
            "sequence": 1,
            "timestampUtc": "2026-01-01T00:00:00+00:00",
            "traceId": "t1",
            "provider": "reference",
            "subjectId": "user-teller1",
            "action": "bank.account.read",
            "resourceType": "account",
            "resourceId": "acc-1",
            "decision": "Permit",
            "reason": "Permit",
            "tenant": "CONTOSO",
            "producer": "pdp",
            "prevHash": "0000",
            "rowHash": "abcdef0123456789"
          }
        ]
        """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, json);
        var client = new AuditClient(AuditHttp(handler));

        var entries = await client.GetEntriesAsync(new AuditQuery());

        var entry = Assert.Single(entries!);
        Assert.Equal(1, entry.Sequence);
        Assert.Equal("bank.account.read", entry.Action);
        Assert.Equal("Permit", entry.Decision);
        Assert.Equal("abcdef0123456789", entry.RowHash);
    }

    [Fact]
    public async Task GetEntriesAsync_parses_empty_list()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "[]");
        var client = new AuditClient(AuditHttp(handler));

        var entries = await client.GetEntriesAsync(new AuditQuery());

        Assert.NotNull(entries);
        Assert.Empty(entries!);
    }

    [Fact]
    public async Task GetEntriesAsync_fails_closed_to_null_on_non_success()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "down");
        var client = new AuditClient(AuditHttp(handler));

        Assert.Null(await client.GetEntriesAsync(new AuditQuery()));
    }

    [Fact]
    public async Task GetEntriesAsync_fails_closed_to_null_on_exception()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("boom"));
        var client = new AuditClient(AuditHttp(handler));

        Assert.Null(await client.GetEntriesAsync(new AuditQuery()));
    }

    // ---- AuditClient.VerifyChainAsync ----

    [Fact]
    public async Task VerifyChainAsync_gets_verify_and_parses_valid_response()
    {
        const string json = """
        { "valid": true, "entryCount": 42, "brokenAtSequence": null, "reason": null,
          "tailSequence": 42, "tailRowHash": "deadbeef" }
        """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, json);
        var client = new AuditClient(AuditHttp(handler));

        var result = await client.VerifyChainAsync();

        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/api/audit/verify", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.NotNull(result);
        Assert.True(result!.Valid);
        Assert.Equal(42, result.EntryCount);
    }

    [Fact]
    public async Task VerifyChainAsync_parses_broken_response()
    {
        const string json = """
        { "valid": false, "entryCount": 10, "brokenAtSequence": 4,
          "reason": "row hash mismatch", "tailSequence": 10, "tailRowHash": "ff" }
        """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, json);
        var client = new AuditClient(AuditHttp(handler));

        var result = await client.VerifyChainAsync();

        Assert.NotNull(result);
        Assert.False(result!.Valid);
        Assert.Equal(4, result.BrokenAtSequence);
        Assert.Equal("row hash mismatch", result.Reason);
    }

    [Fact]
    public async Task VerifyChainAsync_fails_closed_to_null_on_non_success()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.InternalServerError, "err");
        var client = new AuditClient(AuditHttp(handler));

        Assert.Null(await client.VerifyChainAsync());
    }

    [Fact]
    public async Task VerifyChainAsync_fails_closed_to_null_on_malformed_json()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "not json");
        var client = new AuditClient(AuditHttp(handler));

        Assert.Null(await client.VerifyChainAsync());
    }
}
