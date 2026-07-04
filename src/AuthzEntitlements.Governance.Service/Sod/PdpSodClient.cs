using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuthzEntitlements.Governance.Service.Sod;

// HTTP implementation of IPdpSodClient over an Aspire-discovered, resilience-wrapped typed
// HttpClient (see AddServiceDefaults). It POSTs the AuthZEN evaluate contract to the PDP
// and maps the decision to a SodCheckResult.
//
// Fail-closed policy (mirrors Bank.Api's EntitlementsClient): any transport exception,
// timeout, non-success status, or missing/malformed body maps to SodCheckResult.Unavailable
// — never a thrown exception and never a permit — so the approval workflow can deny safely
// when the PDP cannot be reached. Genuine caller cancellation is the only exception that
// propagates.
public sealed class PdpSodClient(HttpClient httpClient) : IPdpSodClient
{
    // The SoD action name agreed with the PDP agent. Do not change without updating the
    // reference/OPA policies that key on it.
    public const string ActionName = "governance.access.request";

    private const string EvaluatePath = "/api/authz/evaluate";
    private const string ResourceType = "access-grant";
    private const string SubjectType = "user";
    private const string PermitDecision = "Permit";
    private const string DenyDecision = "Deny";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<SodCheckResult> EvaluateAsync(
        string principalId,
        string tenantCode,
        IReadOnlyCollection<string> proposedRoles,
        string accessPackageCode,
        CancellationToken ct)
    {
        var request = new PdpAccessRequest(
            new PdpSubject(SubjectType, principalId, proposedRoles.ToArray(), tenantCode),
            new PdpAction(ActionName),
            new PdpResource(ResourceType, accessPackageCode, tenantCode),
            new PdpEvaluationContext([]));

        try
        {
            using var response = await httpClient.PostAsJsonAsync(EvaluatePath, request, JsonOptions, ct);
            if (!response.IsSuccessStatusCode)
            {
                return SodCheckResult.Unavailable(
                    $"pdp returned {(int)response.StatusCode} for the SoD evaluation");
            }

            var decision = await response.Content.ReadFromJsonAsync<PdpAccessDecision>(JsonOptions, ct);
            return Map(decision);
        }
        catch (Exception ex) when (ShouldFailClosed(ex, ct))
        {
            return SodCheckResult.Unavailable($"pdp unreachable: {ex.Message}");
        }
    }

    // Maps a parsed decision to the local result. A permit is a permit; an explicit deny
    // carries the PDP's primary reason; anything else (null body, unknown decision, or a
    // deny with no reason) fails closed as unavailable rather than being read as a permit.
    private static SodCheckResult Map(PdpAccessDecision? decision)
    {
        if (decision?.Decision is null)
        {
            return SodCheckResult.Unavailable("pdp returned an empty SoD decision");
        }

        if (string.Equals(decision.Decision, PermitDecision, StringComparison.OrdinalIgnoreCase))
        {
            return SodCheckResult.Permit;
        }

        if (string.Equals(decision.Decision, DenyDecision, StringComparison.OrdinalIgnoreCase))
        {
            var primary = decision.Reasons is { Count: > 0 } ? decision.Reasons[0] : null;
            if (primary is null)
            {
                return SodCheckResult.Unavailable("pdp denied the SoD check without a reason");
            }

            return SodCheckResult.Deny(primary.Code, primary.Message);
        }

        return SodCheckResult.Unavailable($"pdp returned an unknown SoD decision '{decision.Decision}'");
    }

    // Fail closed for every fault except a cancellation genuinely requested by the caller,
    // which must propagate so an aborted request is not silently treated as "unavailable".
    private static bool ShouldFailClosed(Exception ex, CancellationToken ct) =>
        ex is not OperationCanceledException || !ct.IsCancellationRequested;
}
