using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace AuthzEntitlements.Bank.Web.Clients;

// Typed client for the tamper-evident audit log (query + chain verification). The audit API is
// anonymous in this lab and called intra-cluster, so NO token handler is attached. Reads fail
// closed to null so the Audit Explorer surfaces an explicit "unavailable" state rather than
// presenting a partial or fabricated chain as trustworthy.
public interface IAuditClient
{
    Task<IReadOnlyList<AuditEntryDto>?> GetEntriesAsync(
        AuditQuery query, CancellationToken ct = default);

    Task<ChainVerificationDto?> VerifyChainAsync(CancellationToken ct = default);
}

public sealed class AuditClient(HttpClient http) : IAuditClient
{
    public async Task<IReadOnlyList<AuditEntryDto>?> GetEntriesAsync(
        AuditQuery query, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync(BuildEntriesUri(query), ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<List<AuditEntryDto>>(BankJson.Options, ct);
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

    public async Task<ChainVerificationDto?> VerifyChainAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await http.GetAsync("/api/audit/verify", ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<ChainVerificationDto>(BankJson.Options, ct);
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

    // Builds /api/audit/entries with only the non-null filter fields, each properly URL-encoded.
    // Kept internal + static so the query-string composition is unit-testable without a server.
    internal static string BuildEntriesUri(AuditQuery query)
    {
        var parameters = new Dictionary<string, string?>();

        if (query.Sequence is > 0)
        {
            parameters["sequence"] = query.Sequence.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }

        AddIfPresent(parameters, "subject", query.Subject);
        AddIfPresent(parameters, "action", query.Action);
        AddIfPresent(parameters, "decision", query.Decision);
        AddIfPresent(parameters, "tenant", query.Tenant);
        AddIfPresent(parameters, "trace", query.Trace);
        AddIfPresent(parameters, "producer", query.Producer);

        if (query.Limit is { } limit)
        {
            parameters["limit"] = limit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (query.Offset is { } offset)
        {
            parameters["offset"] = offset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return QueryHelpers.AddQueryString("/api/audit/entries", parameters);
    }

    private static void AddIfPresent(IDictionary<string, string?> parameters, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parameters[key] = value.Trim();
        }
    }
}
