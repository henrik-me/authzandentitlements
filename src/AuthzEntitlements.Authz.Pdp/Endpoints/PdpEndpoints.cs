using AuthzEntitlements.Authz.Pdp.Catalog;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Services;

namespace AuthzEntitlements.Authz.Pdp.Endpoints;

// The AuthZEN Access Evaluation surface for the PDP. Every decision path goes through
// PdpDecisionService so the audit + OTel hooks fire; endpoints never call a provider
// directly. Endpoints are anonymous in CS05 — this is an in-process reference host, not
// yet wired behind the edge/token pipeline.
public static class PdpEndpoints
{
    public static IEndpointRouteBuilder MapPdpEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/authz");

        // AuthZEN Access Evaluation: one request in, one self-explaining decision out.
        group.MapPost("/evaluate", (AccessRequest? request, PdpDecisionService decisions) =>
        {
            // Fail closed on a missing OR structurally-incomplete body: System.Text.Json turns
            // a "{}" body into an AccessRequest whose nested records are null, so a bare null
            // check is not enough — dereferencing those nulls downstream would surface a 500.
            // A malformed request is a 400, never an evaluated decision.
            var validationError = AccessRequestValidation.Validate(request);
            if (validationError is not null)
            {
                return Results.Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Ok(decisions.Evaluate(request!));
        });

        // Lists the shared scenario catalog (ids, descriptions, expected outcomes).
        group.MapGet("/scenarios", () => Results.Ok(
            FintechScenarioCatalog.Scenarios.Select(s => new
            {
                s.Id,
                s.Description,
                expected = s.Expected.ToString(),
                expectedReasonCode = s.ExpectedReasonCode,
            })));

        // Runs the catalog against the active provider and reports per-scenario pass/fail.
        group.MapPost("/scenarios/verify", (PdpDecisionService decisions) =>
        {
            var report = ScenarioCatalogRunner.Run(FintechScenarioCatalog.Scenarios, decisions.Evaluate);
            var body = new
            {
                report.AllPassed,
                report.Passed,
                report.Total,
                results = report.Results.Select(r => new
                {
                    r.Scenario.Id,
                    r.Scenario.Description,
                    expected = r.Scenario.Expected.ToString(),
                    expectedReasonCode = r.Scenario.ExpectedReasonCode,
                    actual = r.Actual.Decision.ToString(),
                    actualReasonCode = r.Actual.Reasons.Count > 0 ? r.Actual.Reasons[0].Code : string.Empty,
                    obligations = r.Actual.Obligations.Select(o => o.Id),
                    r.Passed,
                }),
            };

            // 200 when the active provider answers the whole catalog as expected; 500 marks a
            // provider that disagrees with the reference expectations (a real regression).
            return report.AllPassed
                ? Results.Ok(body)
                : Results.Json(body, statusCode: StatusCodes.Status500InternalServerError);
        });

        return app;
    }
}
