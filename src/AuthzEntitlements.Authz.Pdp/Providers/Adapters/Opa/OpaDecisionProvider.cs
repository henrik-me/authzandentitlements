using System.Text;
using System.Text.Json;
using AuthzEntitlements.Authz.Pdp.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AuthzEntitlements.Authz.Pdp.Providers.Adapters.Opa;

// Adapter over an out-of-process OPA (Open Policy Agent) REST engine. It POSTs the AccessRequest
// (wrapped as {"input": <request>}) to OPA's data API decision path and maps the returned
// {"result":{decision,reason,obligations}} back onto the shared AccessDecision contract, so the
// Rego policy answers the SAME question shape as the in-process reference engine.
//
// The IAuthorizationDecisionProvider contract is synchronous. OPA is reached over HTTP, so this
// provider does sync-over-HTTP via HttpClient.Send (NOT .Result/.Wait/.GetAwaiter().GetResult()) —
// the contract note explicitly sanctions this for out-of-process adapters.
//
// Fail-closed posture: any failure to obtain a well-formed Permit/Deny from OPA — transport error,
// timeout, non-success status, an absent "result" (policy undefined for the input), a missing
// reason, an unrecognized decision, or a JSON parse error — returns Deny with the provider-local
// ProviderUnavailable reason. It never falls through to a permit. The specific cause is LOGGED;
// the AccessDecision returned to (anonymous) /api/authz/evaluate callers carries only a stable,
// non-sensitive message, so internal URLs/network/config detail is never leaked to callers.
public sealed class OpaDecisionProvider : IAuthorizationDecisionProvider
{
    public const string HttpClientName = "opa";

    // Provider-local reason for an unreachable/misbehaving engine. Deliberately NOT added to the
    // shared ReasonCodes: it maps to no Bank.Api rule and never appears in the parity catalog
    // (OPA is reachable and the Rego policy is total there). It exists only so a real outage is a
    // legible, machine-stable Deny rather than an opaque 500.
    private const string ProviderUnavailable = "ProviderUnavailable";

    // Stable, non-sensitive message returned to callers on every fail-closed decision. The specific
    // cause (exception text, remote status phrase, config detail) is logged instead of surfaced, so
    // /api/authz/evaluate — which returns AccessDecision.Reason.Message straight to anonymous
    // callers — cannot leak internal configuration or network details.
    private const string ProviderUnavailableMessage =
        "The OPA authorization engine did not return a usable decision; failing closed.";

    // Web defaults give camelCase serialization (matching the wire contract) AND case-insensitive
    // property matching on the way back in.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly OpaOptions _options;
    private readonly ILogger<OpaDecisionProvider> _logger;

    public OpaDecisionProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<OpaOptions> options,
        ILogger<OpaDecisionProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "opa";

    public AccessDecision Evaluate(AccessRequest request)
    {
        try
        {
            var body = JsonSerializer.Serialize(new OpaInput(request), SerializerOptions);
            var client = _httpClientFactory.CreateClient(HttpClientName);

            using var message = new HttpRequestMessage(HttpMethod.Post, _options.DecisionPath)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };

            using var response = client.Send(message);
            if (!response.IsSuccessStatusCode)
            {
                return FailClosed(
                    $"OPA returned a non-success status {(int)response.StatusCode} " +
                    $"({response.ReasonPhrase}).");
            }

            using var stream = response.Content.ReadAsStream();
            var payload = JsonSerializer.Deserialize<OpaDecisionResponse>(stream, SerializerOptions);

            return MapDecision(payload?.Result);
        }
        catch (HttpRequestException ex)
        {
            return FailClosed($"OPA request failed: {ex.Message}");
        }
        catch (TaskCanceledException ex)
        {
            // HttpClient surfaces a client-side timeout as a cancelled task.
            return FailClosed($"OPA request timed out after {_options.TimeoutSeconds}s: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return FailClosed($"OPA response could not be parsed as JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Backstop: fail closed on ANY other error. Notably, a misconfigured Opa section
            // surfaces here when the named HttpClient is built inside CreateClient — a malformed
            // Opa:BaseUrl throws UriFormatException and a non-positive Opa:TimeoutSeconds throws
            // ArgumentOutOfRangeException. An authorization PDP must never throw through to the
            // caller: every failure to obtain a well-formed decision is a Deny, never a permit or
            // an unhandled 500.
            return FailClosed($"OPA evaluation failed ({ex.GetType().Name}): {ex.Message}");
        }
    }

    private AccessDecision MapDecision(OpaDecisionResult? result)
    {
        // Absent result = policy undefined for the input (OPA returned "{}"): fail closed.
        if (result is null)
        {
            return FailClosed("OPA returned no decision result (policy undefined for the input).");
        }

        if (string.IsNullOrWhiteSpace(result.Reason))
        {
            return FailClosed("OPA decision result was missing a reason code.");
        }

        var reason = new Reason(result.Reason, $"OPA policy decision: {result.Reason}.");

        return result.Decision switch
        {
            "Permit" => AccessDecision.Permit(reason, MapObligations(result.Obligations)),
            "Deny" => AccessDecision.Deny(reason),
            _ => FailClosed($"OPA returned an unrecognized decision '{result.Decision}'."),
        };
    }

    private static Obligation[] MapObligations(IReadOnlyList<string>? obligations)
    {
        if (obligations is null || obligations.Count == 0)
        {
            return [];
        }

        var mapped = new List<Obligation>(obligations.Count);
        foreach (var id in obligations)
        {
            var obligation = MapObligation(id);
            if (obligation is not null)
            {
                mapped.Add(obligation);
            }
        }

        return mapped.ToArray();
    }

    private static Obligation? MapObligation(string id) => id switch
    {
        "require_approval" => new Obligation(ObligationIds.RequireApproval),
        "post_immediately" => new Obligation(ObligationIds.PostImmediately),
        // Unknown obligation strings are dropped rather than propagated as opaque ids.
        _ => null,
    };

    // Log the specific cause for operators/telemetry; return only the stable, non-sensitive message
    // to the caller so no internal detail leaks through the anonymous evaluate endpoint.
    private AccessDecision FailClosed(string diagnostic)
    {
        _logger.LogWarning("OPA adapter failing closed: {Diagnostic}", diagnostic);
        return AccessDecision.Deny(new Reason(ProviderUnavailable, ProviderUnavailableMessage));
    }

    // Wraps the AccessRequest as OPA's expected {"input": <request>} envelope.
    private sealed record OpaInput(AccessRequest Input);
}
