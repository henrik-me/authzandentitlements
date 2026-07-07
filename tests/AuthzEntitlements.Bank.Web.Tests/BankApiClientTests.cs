using System.Net;
using System.Text.Json;
using AuthzEntitlements.Bank.Web.Clients;
using Xunit;

namespace AuthzEntitlements.Bank.Web.Tests;

public class BankApiClientTests
{
    private static HttpClient Client(StubHttpMessageHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://edge-gateway") };

    [Fact]
    public async Task GetAccountsAsync_issues_get_and_maps_dtos()
    {
        const string json = """
        [
          {
            "id": "50000000-0000-0000-0000-000000000001",
            "tenantId": "11111111-1111-1111-1111-111111111111",
            "branchId": "20000000-0000-0000-0000-000000000001",
            "accountNumber": "CONTOSO-CHK-0001",
            "customerName": "Alice Anderson",
            "type": "Checking",
            "balance": 4200.00,
            "currency": "USD",
            "status": "Active"
          }
        ]
        """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, json);
        var client = new BankApiClient(Client(handler), new AuthChallengeState());

        var accounts = await client.GetAccountsAsync();

        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
        Assert.Equal("/api/accounts", handler.LastRequest!.RequestUri!.AbsolutePath);
        var account = Assert.Single(accounts);
        Assert.Equal("CONTOSO-CHK-0001", account.AccountNumber);
        Assert.Equal(AccountType.Checking, account.Type);
        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.Equal(4200.00m, account.Balance);
    }

    [Fact]
    public async Task GetAccountsAsync_fails_closed_to_empty_on_forbidden()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Forbidden, "");
        var client = new BankApiClient(Client(handler), new AuthChallengeState());

        var accounts = await client.GetAccountsAsync();

        Assert.Empty(accounts);
    }

    [Fact]
    public async Task GetAccountAsync_requests_by_id_and_maps()
    {
        const string json = """
        {
          "id": "50000000-0000-0000-0000-000000000002",
          "tenantId": "11111111-1111-1111-1111-111111111111",
          "branchId": "20000000-0000-0000-0000-000000000001",
          "accountNumber": "CONTOSO-SAV-0001",
          "customerName": "Bob Brown",
          "type": "Savings",
          "balance": 58000.00,
          "currency": "USD",
          "status": "Active"
        }
        """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, json);
        var client = new BankApiClient(Client(handler), new AuthChallengeState());
        var id = new Guid("50000000-0000-0000-0000-000000000002");

        var account = await client.GetAccountAsync(id);

        Assert.Equal($"/api/accounts/{id}", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.NotNull(account);
        Assert.Equal(AccountType.Savings, account!.Type);
    }

    [Fact]
    public async Task CreateTransactionAsync_posts_body_and_returns_success()
    {
        const string json = """
        {
          "id": "60000000-0000-0000-0000-000000000009",
          "accountId": "50000000-0000-0000-0000-000000000001",
          "tenantId": "11111111-1111-1111-1111-111111111111",
          "branchId": "20000000-0000-0000-0000-000000000001",
          "type": "Debit",
          "amount": 250.00,
          "currency": "USD",
          "status": "Posted",
          "makerId": "40000000-0000-0000-0000-000000000001",
          "createdAt": "2026-01-02T09:00:00+00:00",
          "reference": "ATM",
          "approval": null
        }
        """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.Created, json);
        var client = new BankApiClient(Client(handler), new AuthChallengeState());
        var req = new CreateTransactionRequest(
            new Guid("50000000-0000-0000-0000-000000000001"),
            TransactionType.Debit,
            250.00m,
            new Guid("40000000-0000-0000-0000-000000000001"),
            "ATM");

        var result = await client.CreateTransactionAsync(req);

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/transactions", handler.LastRequest!.RequestUri!.AbsolutePath);

        using var body = JsonDocument.Parse(handler.LastBody!);
        var root = body.RootElement;
        Assert.Equal("50000000-0000-0000-0000-000000000001", root.GetProperty("accountId").GetString());
        Assert.Equal("Debit", root.GetProperty("type").GetString());
        Assert.Equal(250.00m, root.GetProperty("amount").GetDecimal());

        Assert.True(result.IsSuccess);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal(TransactionStatus.Posted, result.Value!.Status);
    }

    [Fact]
    public async Task CreateTransactionAsync_captures_forbidden_as_failure()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Forbidden, "coarse deny");
        var client = new BankApiClient(Client(handler), new AuthChallengeState());
        var req = new CreateTransactionRequest(
            Guid.NewGuid(), TransactionType.Credit, 10m, Guid.NewGuid(), null);

        var result = await client.CreateTransactionAsync(req);

        Assert.False(result.IsSuccess);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("coarse deny", result.Error);
    }

    [Fact]
    public async Task ApproveTransactionAsync_posts_to_decide_path_with_checker_body()
    {
        const string json = """
        {
          "id": "60000000-0000-0000-0000-000000000002",
          "accountId": "50000000-0000-0000-0000-000000000002",
          "tenantId": "11111111-1111-1111-1111-111111111111",
          "branchId": "20000000-0000-0000-0000-000000000001",
          "type": "Transfer",
          "amount": 15000.00,
          "currency": "USD",
          "status": "Approved",
          "makerId": "40000000-0000-0000-0000-000000000001",
          "createdAt": "2026-01-02T09:00:00+00:00",
          "reference": "Wire",
          "approval": {
            "id": "70000000-0000-0000-0000-000000000002",
            "transactionId": "60000000-0000-0000-0000-000000000002",
            "makerId": "40000000-0000-0000-0000-000000000001",
            "checkerId": "40000000-0000-0000-0000-000000000002",
            "status": "Approved",
            "decisionReason": "ok",
            "requestedAt": "2026-01-02T09:00:00+00:00",
            "decidedAt": "2026-01-02T10:00:00+00:00"
          }
        }
        """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, json);
        var client = new BankApiClient(Client(handler), new AuthChallengeState());
        var id = new Guid("60000000-0000-0000-0000-000000000002");
        var checker = new Guid("40000000-0000-0000-0000-000000000002");

        var result = await client.ApproveTransactionAsync(id, new DecideRequest(checker, "ok"));

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal($"/api/transactions/{id}/approve", handler.LastRequest!.RequestUri!.AbsolutePath);
        using var reqBody = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(checker.ToString(), reqBody.RootElement.GetProperty("checkerId").GetString());
        Assert.True(result.IsSuccess);
        Assert.Equal(ApprovalStatus.Approved, result.Value!.Approval!.Status);
    }

    [Fact]
    public async Task RejectTransactionAsync_maps_conflict_as_failure()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.Conflict, "already decided");
        var client = new BankApiClient(Client(handler), new AuthChallengeState());
        var id = new Guid("60000000-0000-0000-0000-000000000002");

        var result = await client.RejectTransactionAsync(id, new DecideRequest(Guid.NewGuid(), null));

        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal($"/api/transactions/{id}/reject", handler.LastRequest!.RequestUri!.AbsolutePath);
        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task GetAccountsAsync_fails_closed_on_malformed_success_body()
    {
        // A 2xx whose body is not the expected JSON (e.g. an HTML error page) must fail
        // closed to an empty list, not surface an unhandled JsonException.
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "<html>not json</html>");
        var client = new BankApiClient(Client(handler), new AuthChallengeState());

        var accounts = await client.GetAccountsAsync();

        Assert.Empty(accounts);
    }

    [Fact]
    public async Task CreateTransactionAsync_maps_malformed_success_body_as_failure()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "<html>not json</html>");
        var client = new BankApiClient(Client(handler), new AuthChallengeState());

        var result = await client.CreateTransactionAsync(
            new CreateTransactionRequest(Guid.NewGuid(), TransactionType.Debit, 100m, Guid.NewGuid(), null));

        Assert.False(result.IsSuccess);
        Assert.Equal(502, result.StatusCode);
    }
}
