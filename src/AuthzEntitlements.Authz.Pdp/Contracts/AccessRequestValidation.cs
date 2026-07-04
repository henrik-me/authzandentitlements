namespace AuthzEntitlements.Authz.Pdp.Contracts;

// Boundary validation for a deserialized AccessRequest. System.Text.Json can produce an
// AccessRequest whose nested records (or their required collections) are null despite the
// non-null contract types — a "{}" body is the canonical case — so the evaluate endpoint
// validates structural completeness here and fails closed with a 400 rather than letting a
// downstream null dereference surface as a 500. Returns null when the request is complete,
// otherwise a message naming the first missing or blank required field. Required string fields
// use IsNullOrWhiteSpace so a whitespace-only value is rejected at the boundary (400) rather
// than reaching evaluation.
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

        if (string.IsNullOrWhiteSpace(request.Subject.Type))
        {
            return "subject.type is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Subject.Id))
        {
            return "subject.id is required.";
        }

        if (request.Subject.Roles is null)
        {
            return "subject.roles is required (an empty array is allowed).";
        }

        // CS19 on-behalf-of (OBO): the Actor is optional (null => a direct call), but when present it
        // must be structurally complete so the reference provider's delegation constraint reads a real
        // delegate. Fail closed at the boundary rather than let a null delegated-scope list surface
        // downstream. An empty Scopes array is allowed (it satisfies no delegated scope => deny).
        if (request.Subject.Actor is { } actor)
        {
            if (string.IsNullOrWhiteSpace(actor.Type))
            {
                return "subject.actor.type is required when subject.actor is present.";
            }

            if (string.IsNullOrWhiteSpace(actor.Id))
            {
                return "subject.actor.id is required when subject.actor is present.";
            }

            if (actor.Scopes is null)
            {
                return "subject.actor.scopes is required when subject.actor is present (an empty array is allowed).";
            }
        }

        if (request.Action is null)
        {
            return "action is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Action.Name))
        {
            return "action.name is required.";
        }

        if (request.Resource is null)
        {
            return "resource is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Resource.Type))
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
