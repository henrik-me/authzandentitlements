using System.Net.Http.Json;

namespace AuthzEntitlements.Bank.Web.Clients;

// Typed client for the access-governance service (access packages + JIT access
// requests/grants). The service is anonymous and called intra-cluster, so NO token
// handler is attached. Reads fail closed to empty/null; writes capture the outcome into
// an ApiResult (including the segregation-of-duties deny path) without throwing.
public interface IGovernanceClient
{
    Task<IReadOnlyList<AccessPackageResponse>> GetAccessPackagesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<AccessRequestResponse>> GetRequestsAsync(CancellationToken ct = default);

    Task<ApiResult<AccessRequestResponse>> CreateRequestAsync(
        CreateAccessRequestBody body, CancellationToken ct = default);

    Task<ApiResult<AccessGrantResponse>> ApproveRequestAsync(
        Guid id, ApproveRequestBody body, CancellationToken ct = default);

    Task<ApiResult<AccessRequestResponse>> RejectRequestAsync(
        Guid id, RejectRequestBody body, CancellationToken ct = default);

    Task<PrincipalAccessResponse?> GetPrincipalAccessAsync(
        string principalId, CancellationToken ct = default);
}

public sealed class GovernanceClient(HttpClient http) : IGovernanceClient
{
    public Task<IReadOnlyList<AccessPackageResponse>> GetAccessPackagesAsync(CancellationToken ct = default) =>
        GetListAsync<AccessPackageResponse>("/api/governance/access-packages", ct);

    public Task<IReadOnlyList<AccessRequestResponse>> GetRequestsAsync(CancellationToken ct = default) =>
        GetListAsync<AccessRequestResponse>("/api/governance/requests", ct);

    public Task<ApiResult<AccessRequestResponse>> CreateRequestAsync(
        CreateAccessRequestBody body, CancellationToken ct = default) =>
        PostAsync<CreateAccessRequestBody, AccessRequestResponse>("/api/governance/requests", body, ct);

    public Task<ApiResult<AccessGrantResponse>> ApproveRequestAsync(
        Guid id, ApproveRequestBody body, CancellationToken ct = default) =>
        PostAsync<ApproveRequestBody, AccessGrantResponse>(
            $"/api/governance/requests/{id}/approve", body, ct);

    public Task<ApiResult<AccessRequestResponse>> RejectRequestAsync(
        Guid id, RejectRequestBody body, CancellationToken ct = default) =>
        PostAsync<RejectRequestBody, AccessRequestResponse>(
            $"/api/governance/requests/{id}/reject", body, ct);

    public Task<PrincipalAccessResponse?> GetPrincipalAccessAsync(
        string principalId, CancellationToken ct = default) =>
        GetOrNullAsync<PrincipalAccessResponse>(
            $"/api/governance/principals/{Uri.EscapeDataString(principalId)}/access", ct);

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string uri, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(uri, ct);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var value = await response.Content.ReadFromJsonAsync<List<T>>(BankJson.Options, ct);
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

    private async Task<ApiResult<TResponse>> PostAsync<TBody, TResponse>(
        string uri, TBody body, CancellationToken ct)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(uri, body, BankJson.Options, ct);
            var status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                var value = await response.Content.ReadFromJsonAsync<TResponse>(BankJson.Options, ct);
                return value is null
                    ? ApiResult<TResponse>.Failure(status, "The response body was empty.")
                    : ApiResult<TResponse>.Success(value, status);
            }

            var error = await response.Content.ReadAsStringAsync(ct);
            return ApiResult<TResponse>.Failure(status, string.IsNullOrWhiteSpace(error)
                ? response.ReasonPhrase ?? "The request was denied."
                : error);
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<TResponse>.Failure(503, $"The service is unavailable: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ApiResult<TResponse>.Failure(503, "The service did not respond in time.");
        }
    }
}
