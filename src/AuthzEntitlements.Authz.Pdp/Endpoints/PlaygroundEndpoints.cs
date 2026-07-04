using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Playground;
using AuthzEntitlements.Authz.Pdp.Providers;

namespace AuthzEntitlements.Authz.Pdp.Endpoints;

// The AuthZ Playground fan-out endpoint (CS15): run ONE AccessRequest across all engines (or a named
// subset) and return per-engine comparable results. Like the CS17 what-if / shadow surface, this is
// a simulation — it resolves engines by name through the factory and evaluates providers directly,
// so it never emits a real enforcement audit event. Malformed bodies and unknown engine names fail
// closed with a 400 (never a 500 or a wrong-engine result).
public static class PlaygroundEndpoints
{
    public static IEndpointRouteBuilder MapPlaygroundEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/authz");

        // Fan-out: preview what every engine (or the named subset) would decide for one request.
        group.MapPost("/playground/fanout", (PlaygroundFanoutRequest? body,
            PlaygroundFanoutService service, AuthorizationDecisionProviderFactory factory) =>
        {
            if (body is null)
            {
                return Results.Problem(
                    "A fan-out body (request, engines?) is required.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var validationError = AccessRequestValidation.Validate(body.Request);
            if (validationError is not null)
            {
                return Results.Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
            }

            // Validate every named engine up front so a bad name is a 400, not a 500 from the factory's
            // fail-closed GetProvider during the fan-out.
            if (body.Engines is { Count: > 0 })
            {
                foreach (var name in body.Engines)
                {
                    if (!factory.TryGetProvider(name, out _))
                    {
                        return Results.Problem(UnknownEngine(name, factory),
                            statusCode: StatusCodes.Status400BadRequest);
                    }
                }
            }

            return Results.Ok(service.Fanout(body.Request, body.Engines));
        });

        return app;
    }

    private static string UnknownEngine(string name, AuthorizationDecisionProviderFactory factory) =>
        $"Unknown engine '{name}'. Available engines: " +
        $"[{string.Join(", ", factory.ProviderNames.OrderBy(n => n, StringComparer.Ordinal))}].";
}
