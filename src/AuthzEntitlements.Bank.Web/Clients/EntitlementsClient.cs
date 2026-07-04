using System.Net.Http.Json;

namespace AuthzEntitlements.Bank.Web.Clients;

// Typed client for the commercial Entitlements.Service. The service is anonymous and
// called intra-cluster, so NO token handler is attached. Reads fail closed to null so a
// page renders "unavailable" rather than throwing.
public interface IEntitlementsClient
{
    Task<PlanSummaryResponse?> GetPlanAsync(string tenantCode, CancellationToken ct = default);

    Task<FeatureEntitlementResponse?> GetFeatureAsync(
        string tenantCode, string featureKey, CancellationToken ct = default);
}

public sealed class EntitlementsClient(HttpClient http) : IEntitlementsClient
{
    public Task<PlanSummaryResponse?> GetPlanAsync(string tenantCode, CancellationToken ct = default) =>
        GetOrNullAsync<PlanSummaryResponse>(
            $"/api/entitlements/{Uri.EscapeDataString(tenantCode)}/plan", ct);

    public Task<FeatureEntitlementResponse?> GetFeatureAsync(
        string tenantCode, string featureKey, CancellationToken ct = default) =>
        GetOrNullAsync<FeatureEntitlementResponse>(
            $"/api/entitlements/{Uri.EscapeDataString(tenantCode)}/features/{Uri.EscapeDataString(featureKey)}",
            ct);

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
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
