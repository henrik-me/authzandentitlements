namespace AuthzEntitlements.Authz.Pdp.Contracts;

// Boundary validation for a deserialized AccessRequest. System.Text.Json can produce an
// AccessRequest whose nested records (or their required collections) are null despite the
// non-null contract types — a "{}" body is the canonical case — so the evaluate endpoint
// validates structural completeness here and fails closed with a 400 rather than letting a
// downstream null dereference surface as a 500. Returns null when the request is complete,
// otherwise a message naming the first missing or empty required field.
public static class AccessRequestValidation
{
    public static string? Validate(AccessRequest? request)
    {
        if (request is null)
        {
            return "An AccessRequest body (subject, action, resource, context) is required.";
        }

        if (request.Subject is null)
        {
            return "subject is required.";
        }

        if (string.IsNullOrEmpty(request.Subject.Type))
        {
            return "subject.type is required.";
        }

        if (string.IsNullOrEmpty(request.Subject.Id))
        {
            return "subject.id is required.";
        }

        if (request.Subject.Roles is null)
        {
            return "subject.roles is required (an empty array is allowed).";
        }

        if (request.Action is null)
        {
            return "action is required.";
        }

        if (string.IsNullOrEmpty(request.Action.Name))
        {
            return "action.name is required.";
        }

        if (request.Resource is null)
        {
            return "resource is required.";
        }

        if (string.IsNullOrEmpty(request.Resource.Type))
        {
            return "resource.type is required.";
        }

        if (request.Context is null)
        {
            return "context is required.";
        }

        if (request.Context.Scopes is null)
        {
            return "context.scopes is required (an empty array is allowed).";
        }

        return null;
    }
}
