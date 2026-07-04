using System.Text.Json;

namespace AuthzEntitlements.Compliance;

// The HTTP-backed IGovernanceClient. Each call GETs a Governance endpoint under a short,
// cancellation-bounded timeout. A transport failure or timeout becomes a
// GovernanceUnreachableException (the caller self-skips offline); a REACHED response that is a
// non-success status OR a malformed body becomes a ComplianceDataException (fail-closed —
// surfaces as a non-zero exit). Never returns a silent default for a response it could not parse,
// and never treats a reached-but-erroring service as "offline".
public sealed class HttpGovernanceClient : IGovernanceClient, IDisposable
{
    private const string CampaignsPath = "/api/governance/review-campaigns";
    private const string AccessPackagesPath = "/api/governance/access-packages";

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public HttpGovernanceClient(string baseUrl, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl, UriKind.Absolute),
            Timeout = timeout ?? TimeSpan.FromSeconds(3),
        };
        _ownsClient = true;
    }

    // Test/DI seam: use a caller-provided HttpClient (with its own BaseAddress/Timeout).
    public HttpGovernanceClient(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _ownsClient = false;
    }

    public Task<IReadOnlyList<ReviewCampaignDto>> GetCampaignsAsync(CancellationToken cancellationToken) =>
        GetArrayAsync<ReviewCampaignDto>(CampaignsPath, cancellationToken);

    public Task<IReadOnlyList<AccessPackageDto>> GetAccessPackagesAsync(CancellationToken cancellationToken) =>
        GetArrayAsync<AccessPackageDto>(AccessPackagesPath, cancellationToken);

    public Task<IReadOnlyList<AccessGrantDto>> GetPrincipalGrantsAsync(
        string principalId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        var path = $"/api/governance/principals/{Uri.EscapeDataString(principalId)}/grants";
        return GetArrayAsync<AccessGrantDto>(path, cancellationToken);
    }

    private async Task<IReadOnlyList<T>> GetArrayAsync<T>(string path, CancellationToken cancellationToken)
    {
        string body;
        try
        {
            using var response = await _http.GetAsync(path, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // The service WAS reached but returned an error (500/401/404/...). Fail closed —
                // this is NOT an offline self-skip: a running-but-erroring governance service must
                // surface as a non-zero exit, never as silently-absent evidence.
                throw new ComplianceDataException(
                    $"GET {path} returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");
            }

            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // An HttpClient timeout surfaces as a cancelled task whose token is NOT the caller's —
            // the service did not respond in time, so self-skip. A GENUINE caller cancellation
            // (cancellationToken.IsCancellationRequested) does not match this filter and propagates,
            // honoring cancellation semantics rather than masquerading as "service offline".
            throw new GovernanceUnreachableException(
                $"GET {path} timed out: {ex.Message}", ex);
        }
        catch (HttpRequestException ex)
        {
            // A connection/DNS failure means the service is not reachable — self-skip.
            throw new GovernanceUnreachableException(
                $"GET {path} failed: {ex.Message}", ex);
        }

        // The response WAS received: parse it fail-closed. A malformed or null body is a hard error,
        // never a silently-empty result.
        T[]? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<T[]>(body, ComplianceJson.Options);
        }
        catch (JsonException ex)
        {
            throw new ComplianceDataException(
                $"Governance response from '{path}' is not valid JSON: {ex.Message}", ex);
        }

        if (parsed is null)
        {
            throw new ComplianceDataException(
                $"Governance response from '{path}' deserialized to null.");
        }

        return parsed;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
