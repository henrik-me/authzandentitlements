using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuthzEntitlements.Bank.Api.Entitlements;

// HTTP implementation of IEntitlementsClient over an Aspire-discovered, resilience-wrapped
// typed HttpClient (see AddServiceDefaults). Deserialization uses the ASP.NET web defaults
// (camelCase, case-insensitive) plus string-enum handling to match the service contract.
//
// Fail-closed policy: any transport exception, timeout, or non-success status maps to the
// record's Unavailable sentinel — never a thrown exception — so entitlement enforcement can
// deny safely when the service cannot be reached. Genuine caller cancellation is the only
// exception that propagates.
public sealed class EntitlementsClient(HttpClient httpClient) : IEntitlementsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<ModuleCheckResult> CheckModuleAsync(string tenantCode, string moduleKey, CancellationToken ct)
    {
        var path = $"/api/entitlements/{Encode(tenantCode)}/modules/{Encode(moduleKey)}";
        try
        {
            using var response = await httpClient.GetAsync(path, ct);
            if (!response.IsSuccessStatusCode)
            {
                return ModuleCheckResult.Unavailable(StatusReason("module", response.StatusCode));
            }

            var result = await response.Content.ReadFromJsonAsync<ModuleCheckResult>(JsonOptions, ct);
            return result ?? ModuleCheckResult.Unavailable("entitlements service returned an empty module response");
        }
        catch (Exception ex) when (ShouldFailClosed(ex, ct))
        {
            return ModuleCheckResult.Unavailable($"entitlements service unreachable: {ex.Message}");
        }
    }

    public async Task<FeatureCheckResult> CheckFeatureAsync(string tenantCode, string featureKey, CancellationToken ct)
    {
        var path = $"/api/entitlements/{Encode(tenantCode)}/features/{Encode(featureKey)}";
        try
        {
            using var response = await httpClient.GetAsync(path, ct);
            if (!response.IsSuccessStatusCode)
            {
                return FeatureCheckResult.Unavailable(StatusReason("feature", response.StatusCode));
            }

            var result = await response.Content.ReadFromJsonAsync<FeatureCheckResult>(JsonOptions, ct);
            return result ?? FeatureCheckResult.Unavailable("entitlements service returned an empty feature response");
        }
        catch (Exception ex) when (ShouldFailClosed(ex, ct))
        {
            return FeatureCheckResult.Unavailable($"entitlements service unreachable: {ex.Message}");
        }
    }

    public async Task<QuotaDecisionResult> ConsumeQuotaAsync(
        string tenantCode, string quotaKey, long amount, CancellationToken ct)
    {
        var path = $"/api/entitlements/{Encode(tenantCode)}/quotas/{Encode(quotaKey)}/consume";
        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                path, new ConsumeQuotaRequest(amount), JsonOptions, ct);
            if (!response.IsSuccessStatusCode)
            {
                return QuotaDecisionResult.Unavailable(StatusReason("quota", response.StatusCode));
            }

            var result = await response.Content.ReadFromJsonAsync<QuotaDecisionResult>(JsonOptions, ct);
            return result ?? QuotaDecisionResult.Unavailable("entitlements service returned an empty quota response");
        }
        catch (Exception ex) when (ShouldFailClosed(ex, ct))
        {
            return QuotaDecisionResult.Unavailable($"entitlements service unreachable: {ex.Message}");
        }
    }

    public async Task<SeatUsageResult> GetSeatsAsync(string tenantCode, CancellationToken ct)
    {
        var path = $"/api/entitlements/{Encode(tenantCode)}/seats";
        try
        {
            using var response = await httpClient.GetAsync(path, ct);
            if (!response.IsSuccessStatusCode)
            {
                return SeatUsageResult.Unavailable(StatusReason("seats", response.StatusCode));
            }

            var result = await response.Content.ReadFromJsonAsync<SeatUsageResult>(JsonOptions, ct);
            return result ?? SeatUsageResult.Unavailable("entitlements service returned an empty seats response");
        }
        catch (Exception ex) when (ShouldFailClosed(ex, ct))
        {
            return SeatUsageResult.Unavailable($"entitlements service unreachable: {ex.Message}");
        }
    }

    // Fail closed for every fault except a cancellation genuinely requested by the caller,
    // which must propagate so an aborted request is not silently treated as "unavailable".
    private static bool ShouldFailClosed(Exception ex, CancellationToken ct) =>
        ex is not OperationCanceledException || !ct.IsCancellationRequested;

    private static string StatusReason(string gate, System.Net.HttpStatusCode status) =>
        $"entitlements service returned {(int)status} for {gate} check";

    private static string Encode(string value) => Uri.EscapeDataString(value);

    private sealed record ConsumeQuotaRequest(long Amount);
}
