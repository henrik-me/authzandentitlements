using System.Globalization;
using System.Text.Json;
using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Lifecycle.AuthZen;

// Maps between the AuthZEN Authorization API wire shape and the PDP's internal AccessRequest /
// AccessDecision (CS17). Inbound: pull the fintech attributes out of the AuthZEN `properties`
// bags into the typed Subject/Resource/Context members the engines read. Outbound: project a
// decision to the AuthZEN boolean + explainability context. ToWireRequest demonstrates the
// reverse projection (our request -> AuthZEN request) so conformance tests can round-trip a
// catalog request through the exact wire format and back.
public static class AuthZenMapper
{
    // Well-known property-bag keys for the fintech attributes carried inside the AuthZEN
    // subject/resource/context `properties` objects. snake_case per AuthZEN property convention.
    public const string RolesKey = "roles";
    public const string TenantKey = "tenant";
    public const string BranchKey = "branch";
    public const string AmountKey = "amount";
    public const string MakerIdKey = "maker_id";
    public const string StatusKey = "status";
    public const string ScopesKey = "scopes";

    public static AccessRequest ToAccessRequest(AuthZenEvaluationRequest request)
    {
        var subjectProps = request.Subject.Properties;
        var resourceProps = request.Resource.Properties;
        var contextProps = request.Context;

        var subject = new Subject(
            request.Subject.Type,
            request.Subject.Id,
            GetStringArray(subjectProps, RolesKey),
            GetString(subjectProps, TenantKey),
            GetString(subjectProps, BranchKey));

        var resource = new Resource(
            request.Resource.Type,
            request.Resource.Id,
            GetString(resourceProps, TenantKey),
            GetString(resourceProps, BranchKey),
            GetDecimal(resourceProps, AmountKey),
            GetString(resourceProps, MakerIdKey),
            GetString(resourceProps, StatusKey));

        return new AccessRequest(
            subject,
            new ActionRequest(request.Action.Name),
            resource,
            new EvaluationContext(GetStringArray(contextProps, ScopesKey)));
    }

    public static AuthZenEvaluationResponse ToResponse(AccessDecision decision)
    {
        var reasonCode = decision.Reasons.Count > 0 ? decision.Reasons[0].Code : string.Empty;
        return new AuthZenEvaluationResponse(
            decision.Decision == Decision.Permit,
            new AuthZenDecisionContext(
                reasonCode,
                decision.Reasons.Select(r => r.Message).ToList(),
                decision.Obligations.Select(o => o.Id).ToList()));
    }

    // Project an internal AccessRequest back to the AuthZEN wire request as a serializable object
    // tree. Optional attributes are omitted when null so the emitted `properties` bag is minimal.
    // Used by conformance tests to round-trip a catalog request through the exact AuthZEN shape.
    public static object ToWireRequest(AccessRequest request)
    {
        var subjectProps = new Dictionary<string, object?> { [RolesKey] = request.Subject.Roles };
        AddIfNotNull(subjectProps, TenantKey, request.Subject.Tenant);
        AddIfNotNull(subjectProps, BranchKey, request.Subject.Branch);

        var resourceProps = new Dictionary<string, object?>();
        AddIfNotNull(resourceProps, TenantKey, request.Resource.Tenant);
        AddIfNotNull(resourceProps, BranchKey, request.Resource.Branch);
        if (request.Resource.Amount is { } amount)
        {
            resourceProps[AmountKey] = amount;
        }

        AddIfNotNull(resourceProps, MakerIdKey, request.Resource.MakerId);
        AddIfNotNull(resourceProps, StatusKey, request.Resource.Status);

        return new
        {
            subject = new { type = request.Subject.Type, id = request.Subject.Id, properties = subjectProps },
            action = new { name = request.Action.Name },
            resource = new { type = request.Resource.Type, id = request.Resource.Id, properties = resourceProps },
            context = new Dictionary<string, object?> { [ScopesKey] = request.Context.Scopes },
        };
    }

    private static void AddIfNotNull(Dictionary<string, object?> bag, string key, string? value)
    {
        if (value is not null)
        {
            bag[key] = value;
        }
    }

    private static string? GetString(Dictionary<string, JsonElement>? props, string key)
    {
        if (props is null || !props.TryGetValue(key, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            _ => null,
        };
    }

    private static IReadOnlyList<string> GetStringArray(Dictionary<string, JsonElement>? props, string key)
    {
        if (props is null || !props.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } value)
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static decimal? GetDecimal(Dictionary<string, JsonElement>? props, string key)
    {
        if (props is null || !props.TryGetValue(key, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(
                element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }
}
