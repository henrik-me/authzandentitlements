using System.Net.Http.Json;

namespace AuthzEntitlements.Bank.Web.Clients;

// Typed client for the fine-grained PDP (native AuthZEN Access Evaluation contract). The
// PDP is anonymous in this lab, so NO token handler is attached. A non-success response
// or transport error fails closed to null so the caller denies rather than assuming a
// Permit.
public interface IPdpClient
{
    Task<PdpDecisionDto?> EvaluateAsync(PdpAccessRequestDto req, CancellationToken ct = default);

    // Fan one AccessRequest out across every engine (or the named subset) for the playground
    // comparison view. Fails closed to null on non-success / transport / JSON error.
    Task<PlaygroundFanoutResponseDto?> FanoutAsync(
        PlaygroundFanoutRequestDto req, CancellationToken ct = default);
}

public sealed class PdpClient(HttpClient http) : IPdpClient
{
    public async Task<PdpDecisionDto?> EvaluateAsync(
        PdpAccessRequestDto req, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                "/api/authz/evaluate", req, BankJson.Options, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<PdpDecisionDto>(BankJson.Options, ct);
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

    public async Task<PlaygroundFanoutResponseDto?> FanoutAsync(
        PlaygroundFanoutRequestDto req, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.PostAsJsonAsync(
                "/api/authz/playground/fanout", req, BankJson.Options, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<PlaygroundFanoutResponseDto>(
                BankJson.Options, ct);
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
