using System.Net.Http.Json;

namespace AuthzEntitlements.Bank.Web.Clients;

// Typed client for the access-governance service (access packages + JIT access
// requests/grants). As of CS29 the access-request endpoints (create/list/get/approve/reject)
// are tenant-scoped and require the user's Keycloak token, so AccessTokenHandler is attached at
// registration (Program.cs) to forward it; the anonymous read endpoints (access-packages,
// principals) simply ignore it. Reads fail closed to empty/null; writes capture the outcome into
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

    // ---- CS21 break-glass grant lifecycle (anonymous endpoints; reads fail closed to empty) ----

    Task<ApiResult<BreakGlassGrantResponse>> IssueBreakGlassAsync(
        IssueBreakGlassRequest body, CancellationToken ct = default);

    Task<IReadOnlyList<BreakGlassGrantResponse>> GetBreakGlassGrantsAsync(
        bool activeOnly = false, CancellationToken ct = default);

    Task<IReadOnlyList<BreakGlassGrantResponse>> GetBreakGlassPendingReviewAsync(
        CancellationToken ct = default);

    Task<ApiResult<BreakGlassGrantResponse>> ReviewBreakGlassAsync(
        Guid id, ReviewBreakGlassRequest body, CancellationToken ct = default);

    // ---- CS21 manager->delegate delegation grant lifecycle ----

    Task<ApiResult<DelegationGrantResponse>> CreateDelegationAsync(
        CreateDelegationRequest body, CancellationToken ct = default);

    Task<IReadOnlyList<DelegationGrantResponse>> GetDelegationsAsync(
        bool activeOnly = false, CancellationToken ct = default);

    Task<ApiResult<DelegationGrantResponse>> RevokeDelegationAsync(
        Guid id, RevokeDelegationRequest body, CancellationToken ct = default);
}

public sealed class GovernanceClient(HttpClient http, AuthChallengeState authChallenge) : IGovernanceClient
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

    public Task<ApiResult<BreakGlassGrantResponse>> IssueBreakGlassAsync(
        IssueBreakGlassRequest body, CancellationToken ct = default) =>
        PostAsync<IssueBreakGlassRequest, BreakGlassGrantResponse>(
            "/api/governance/break-glass", body, ct);

    public Task<IReadOnlyList<BreakGlassGrantResponse>> GetBreakGlassGrantsAsync(
        bool activeOnly = false, CancellationToken ct = default) =>
        GetListAsync<BreakGlassGrantResponse>(
            activeOnly ? "/api/governance/break-glass?activeOnly=true" : "/api/governance/break-glass", ct);

    public Task<IReadOnlyList<BreakGlassGrantResponse>> GetBreakGlassPendingReviewAsync(
        CancellationToken ct = default) =>
        GetListAsync<BreakGlassGrantResponse>("/api/governance/break-glass/pending-review", ct);

    public Task<ApiResult<BreakGlassGrantResponse>> ReviewBreakGlassAsync(
        Guid id, ReviewBreakGlassRequest body, CancellationToken ct = default) =>
        PostAsync<ReviewBreakGlassRequest, BreakGlassGrantResponse>(
            $"/api/governance/break-glass/{id}/review", body, ct);

    public Task<ApiResult<DelegationGrantResponse>> CreateDelegationAsync(
        CreateDelegationRequest body, CancellationToken ct = default) =>
        PostAsync<CreateDelegationRequest, DelegationGrantResponse>(
            "/api/governance/delegations", body, ct);

    public Task<IReadOnlyList<DelegationGrantResponse>> GetDelegationsAsync(
        bool activeOnly = false, CancellationToken ct = default) =>
        GetListAsync<DelegationGrantResponse>(
            activeOnly ? "/api/governance/delegations?activeOnly=true" : "/api/governance/delegations", ct);

    public Task<ApiResult<DelegationGrantResponse>> RevokeDelegationAsync(
        Guid id, RevokeDelegationRequest body, CancellationToken ct = default) =>
        PostAsync<RevokeDelegationRequest, DelegationGrantResponse>(
            $"/api/governance/delegations/{id}/revoke", body, ct);

    private async Task<IReadOnlyList<T>> GetListAsync<T>(string uri, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(uri, ct);
            if (!response.IsSuccessStatusCode)
            {
                authChallenge.Capture(response);
                return [];
            }

            var value = await response.Content.ReadFromJsonAsync<List<T>>(BankJson.Options, ct);
            return value ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
        catch (System.Text.Json.JsonException)
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
                authChallenge.Capture(response);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<T>(BankJson.Options, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (System.Text.Json.JsonException)
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
            var challenge = authChallenge.Capture(response);
            return ApiResult<TResponse>.Failure(
                status, AuthChallengeState.DescribeFailure(challenge, error, response.ReasonPhrase));
        }
        catch (HttpRequestException ex)
        {
            return ApiResult<TResponse>.Failure(503, $"The service is unavailable: {ex.Message}");
        }
        catch (System.Text.Json.JsonException)
        {
            return ApiResult<TResponse>.Failure(502, "The service returned a malformed response.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ApiResult<TResponse>.Failure(503, "The service did not respond in time.");
        }
    }
}
