using System.Net.Http.Json;

namespace AuthzEntitlements.Bank.Web.Clients;

// Typed client for the fintech domain API. Every call is routed THROUGH the edge
// gateway (base address "https+http://edge-gateway") so the coarse authorization layer
// is exercised on the read/write path, and the AccessTokenHandler (attached to this
// client only) forwards the user's bearer token.
//
// Read (GET) helpers fail closed to empty/null so a page renders an "unavailable / not
// authorized" state instead of throwing. Write (POST) helpers never throw on non-2xx:
// they capture the status + body into an ApiResult so coarse/fine/entitlement/decide-once
// denials are visible outcomes.
public interface IBankApiClient
{
    Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default);

    Task<AccountDto?> GetAccountAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<TransactionDto>> GetTransactionsAsync(CancellationToken ct = default);

    Task<TransactionDto?> GetTransactionAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken ct = default);

    Task<ApiResult<TransactionDto>> CreateTransactionAsync(
        CreateTransactionRequest req, CancellationToken ct = default);

    Task<ApiResult<TransactionDto>> ApproveTransactionAsync(
        Guid id, DecideRequest req, CancellationToken ct = default);

    Task<ApiResult<TransactionDto>> RejectTransactionAsync(
        Guid id, DecideRequest req, CancellationToken ct = default);
}

public sealed class BankApiClient(HttpClient http) : IBankApiClient
{
    public Task<IReadOnlyList<AccountDto>> GetAccountsAsync(CancellationToken ct = default) =>
        GetListAsync<AccountDto>("/api/accounts", ct);

    public Task<AccountDto?> GetAccountAsync(Guid id, CancellationToken ct = default) =>
        GetOrNullAsync<AccountDto>($"/api/accounts/{id}", ct);

    public Task<IReadOnlyList<TransactionDto>> GetTransactionsAsync(CancellationToken ct = default) =>
        GetListAsync<TransactionDto>("/api/transactions", ct);

    public Task<TransactionDto?> GetTransactionAsync(Guid id, CancellationToken ct = default) =>
        GetOrNullAsync<TransactionDto>($"/api/transactions/{id}", ct);

    public Task<IReadOnlyList<UserDto>> GetUsersAsync(CancellationToken ct = default) =>
        GetListAsync<UserDto>("/api/users", ct);

    public Task<ApiResult<TransactionDto>> CreateTransactionAsync(
        CreateTransactionRequest req, CancellationToken ct = default) =>
        PostAsync("/api/transactions", req, ct);

    public Task<ApiResult<TransactionDto>> ApproveTransactionAsync(
        Guid id, DecideRequest req, CancellationToken ct = default) =>
        PostAsync($"/api/transactions/{id}/approve", req, ct);

    public Task<ApiResult<TransactionDto>> RejectTransactionAsync(
        Guid id, DecideRequest req, CancellationToken ct = default) =>
        PostAsync($"/api/transactions/{id}/reject", req, ct);

    // GET a collection; fail closed to an empty list on any non-success or transport error
    // so the page shows an "unavailable / not authorized" state rather than throwing.
    private async Task<IReadOnlyList<T>> GetListAsync<T>(string uri, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(uri, ct);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var value = await response.Content
                .ReadFromJsonAsync<List<T>>(BankJson.Options, ct);
            return value ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return [];
        }
    }

    // GET a single resource; fail closed to null on any non-success or transport error.
    private async Task<T?> GetOrNullAsync<T>(string uri, CancellationToken ct)
        where T : class
    {
        try
        {
            using var response = await http.GetAsync(uri, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<T>(BankJson.Options, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    // POST a write; capture the outcome into an ApiResult without throwing on non-2xx so
    // the UI can render coarse/fine/entitlement/decide-once denials as visible outcomes.
    private async Task<ApiResult<TransactionDto>> PostAsync<TBody>(
        string uri, TBody body, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(uri, body, BankJson.Options, ct);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content
                    .ReadFromJsonAsync<TransactionDto>(BankJson.Options, ct);
                return value is null
                    ? ApiResult<TransactionDto>.Failure(status, "The response body was empty.")
                    : ApiResult<TransactionDto>.Success(value, status);
            }

            var error = await response.Content.ReadAsStringAsync(ct);
            return ApiResult<TransactionDto>.Failure(status, string.IsNullOrWhiteSpace(error)
                ? response.ReasonPhrase ?? "The request was denied."
                : error);
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<TransactionDto>.Failure(503, $"The service is unavailable: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ApiResult<TransactionDto>.Failure(503, "The service did not respond in time.");
        }
    }
}
