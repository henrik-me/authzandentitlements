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
            // The AuthZEN boundary is untrusted external input: validate the shape AND the
            // action-required attributes (fail closed) before it can become a real audited decision.
            var shapeError = AuthZenRequestValidation.Validate(request);
            if (shapeError is not null)
            {
                return Results.Problem(shapeError, statusCode: StatusCodes.Status400BadRequest);
            }

            var accessRequest = AuthZenMapper.ToAccessRequest(request!);

            // Second net: the mapped request must still be structurally complete (parity with
            // /evaluate).
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
}
