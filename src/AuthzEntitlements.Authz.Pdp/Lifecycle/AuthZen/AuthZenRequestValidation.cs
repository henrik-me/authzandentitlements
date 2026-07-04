using System.Globalization;
using System.Text.Json;
using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Lifecycle.AuthZen;

// Fail-closed validation for an inbound AuthZEN Access Evaluation request BEFORE it becomes a real
// audited decision (CS17). The AuthZEN endpoint is an UNTRUSTED external wire boundary, so — unlike
// the in-process /evaluate path, which takes an already-typed AccessRequest — it must not rely on
// AuthZenMapper's lenient safe defaults: a present-but-unparseable `amount` would coerce to null and
// then to $0 (silently slipping under the approval threshold), and an omitted `maker_id` on an
// approval would pass segregation-of-duties (SubjectIsMaker is false when MakerId is null). This
// validates the request shape AND the attributes each action's rules key on, returning a message
// naming the first problem (=> 400) or null when the request is safe to evaluate.
public static class AuthZenRequestValidation
{
    public static string? Validate(AuthZenEvaluationRequest? request)
    {
        // Shape: System.Text.Json can leave the nested records null despite the non-null contract
        // types (a "{}"/partial body), so guard before any dereference.
        if (request is null)
        {
            return "An AuthZEN evaluation request (subject, action, resource) is required.";
        }

        if (request.Subject is null
            || string.IsNullOrWhiteSpace(request.Subject.Type)
            || string.IsNullOrWhiteSpace(request.Subject.Id))
        {
            return "subject.type and subject.id are required.";
        }

        if (request.Action is null || string.IsNullOrWhiteSpace(request.Action.Name))
        {
            return "action.name is required.";
        }

        if (request.Resource is null || string.IsNullOrWhiteSpace(request.Resource.Type))
        {
            return "resource.type is required.";
        }

        var props = request.Resource.Properties;

        // A present `amount` must be parseable for ANY action: a garbage amount must never silently
        // coerce to $0 and slip past the approval threshold.
        if (AmountPresent(props) && !AmountParseable(props))
        {
            return "resource.properties.amount must be a number when present.";
        }

        // Action-aware required attributes: the transaction rules key on amount / maker_id / status,
        // so the untrusted boundary must not let them be omitted into a lenient default.
        switch (request.Action.Name)
        {
            case ActionNames.TransactionCreate:
                if (!AmountPresent(props))
                {
                    return "resource.properties.amount is required for bank.transaction.create.";
                }

                if (!HasNonBlankValue(props, AuthZenMapper.MakerIdKey))
                {
                    return "resource.properties.maker_id is required for bank.transaction.create.";
                }

                break;

            case ActionNames.TransactionApprove:
            case ActionNames.TransactionReject:
                if (!HasNonBlankValue(props, AuthZenMapper.MakerIdKey))
                {
                    return $"resource.properties.maker_id is required for {request.Action.Name}.";
                }

                if (!HasNonBlankValue(props, AuthZenMapper.StatusKey))
                {
                    return $"resource.properties.status is required for {request.Action.Name}.";
                }

                break;
        }

        return null;
    }

    private static bool AmountPresent(Dictionary<string, JsonElement>? props) =>
        props is not null && props.ContainsKey(AuthZenMapper.AmountKey);

    private static bool AmountParseable(Dictionary<string, JsonElement>? props)
    {
        var element = props![AuthZenMapper.AmountKey];
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDecimal(out _),
            JsonValueKind.String => decimal.TryParse(
                element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out _),
            _ => false,
        };
    }

    private static bool HasNonBlankValue(Dictionary<string, JsonElement>? props, string key)
    {
        if (props is null || !props.TryGetValue(key, out var element))
        {
            return false;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => !string.IsNullOrWhiteSpace(element.GetString()),
            JsonValueKind.Number => true,
            _ => false,
        };
    }
}
