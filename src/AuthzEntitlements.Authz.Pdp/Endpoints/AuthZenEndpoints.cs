using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Lifecycle.AuthZen;
using AuthzEntitlements.Authz.Pdp.Services;

namespace AuthzEntitlements.Authz.Pdp.Endpoints;

// AuthZEN Authorization API 1.0 "Access Evaluation" surface (CS17). Speaks the native AuthZEN
// wire shape (subject/action/resource with property bags -> boolean decision + context), maps it
// onto the internal AccessRequest, and evaluates through PdpDecisionService so the audit + OTel
// hooks fire — this is a real decision, not a simulation. Fails closed (400) on a malformed body.
public static class AuthZenEndpoints
{
    public static IEndpointRouteBuilder MapAuthZenEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/authz/authzen/evaluation",
            (AuthZenEvaluationRequest? request, PdpDecisionService decisions) =>
        {
            var shapeError = ValidateShape(request);
            if (shapeError is not null)
            {
                return Results.Problem(shapeError, statusCode: StatusCodes.Status400BadRequest);
            }

            var accessRequest = AuthZenMapper.ToAccessRequest(request!);

            // Second net: the mapped request must still be structurally complete (e.g. a blank
            // resource.type would already be caught above, but this keeps parity with /evaluate).
            var validationError = AccessRequestValidation.Validate(accessRequest);
            if (validationError is not null)
            {
                return Results.Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
            }

            var decision = decisions.Evaluate(accessRequest);
            return Results.Ok(AuthZenMapper.ToResponse(decision));
        });

        return app;
    }

    // Validate the AuthZEN request shape before mapping so a "{}"/partial body is a 400 rather than
    // a null-dereference 500. System.Text.Json can leave the nested records null despite the
    // non-null contract types.
    private static string? ValidateShape(AuthZenEvaluationRequest? request)
    {
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

        return null;
    }
}
