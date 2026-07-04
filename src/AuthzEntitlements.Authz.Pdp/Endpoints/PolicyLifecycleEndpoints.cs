using AuthzEntitlements.Authz.Pdp.Catalog;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Lifecycle;
using AuthzEntitlements.Authz.Pdp.Providers;
using AuthzEntitlements.Authz.Pdp.Services;

namespace AuthzEntitlements.Authz.Pdp.Endpoints;

// Policy-lifecycle surface (CS17): what-if simulation, shadow / dual-run engine comparison, and
// the golden-snapshot policy version + drift status. What-if and shadow are simulations/
// comparisons — they resolve engines by name through the factory and evaluate providers directly,
// so they never emit a real enforcement audit event. All engine-name inputs fail closed with a
// 400 (never a 500 or a wrong-engine result). Malformed request bodies are 400.
public static class PolicyLifecycleEndpoints
{
    public static IEndpointRouteBuilder MapPolicyLifecycleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/authz");

        // What-if: preview what a chosen engine (or the active one) would decide for a hypothetical
        // request. A simulation, not an enforced decision.
        group.MapPost("/whatif", (WhatIfRequest? body, WhatIfEvaluator evaluator,
            AuthorizationDecisionProviderFactory factory) =>
        {
            if (body is null)
            {
                return Results.Problem(
                    "A what-if body (engine?, request) is required.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var validationError = AccessRequestValidation.Validate(body.Request);
            if (validationError is not null)
            {
                return Results.Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
            }

            if (!string.IsNullOrWhiteSpace(body.Engine) && !factory.TryGetProvider(body.Engine, out _))
            {
                return Results.Problem(UnknownEngine(body.Engine!, factory),
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Ok(evaluator.Evaluate(body.Engine, body.Request));
        });

        // Shadow / dual-run for a single request: compare a primary engine (or the active one) to
        // one or more shadow engines. Blank shadows falls back to the deterministic in-process RBAC
        // family. Every engine name is validated up front (400 on any unknown name).
        group.MapPost("/shadow", (ShadowRunRequest? body, ShadowRunner runner,
            AuthorizationDecisionProviderFactory factory, PdpDecisionService active) =>
        {
            if (body is null)
            {
                return Results.Problem(
                    "A shadow body (primary?, shadows?, request) is required.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var validationError = AccessRequestValidation.Validate(body.Request);
            if (validationError is not null)
            {
                return Results.Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
            }

            var primary = string.IsNullOrWhiteSpace(body.Primary) ? active.ProviderName : body.Primary.Trim();
            var requested = body.Shadows is { Count: > 0 } ? body.Shadows : ShadowRunner.DeterministicRbacFamily;
            var shadows = requested
                .Where(name => !string.IsNullOrWhiteSpace(name)
                    && !string.Equals(name, primary, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var name in shadows.Prepend(primary))
            {
                if (!factory.TryGetProvider(name, out _))
                {
                    return Results.Problem(UnknownEngine(name, factory),
                        statusCode: StatusCodes.Status400BadRequest);
                }
            }

            if (shadows.Count == 0)
            {
                return Results.Problem(
                    "No shadow engines to compare against the primary.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            return Results.Ok(runner.Run(primary, shadows, body.Request));
        });

        // Whole-catalog dual run: shadow one engine against another across the full parity catalog,
        // reporting per-scenario divergences. The migration/rollout parity check on identical input.
        group.MapPost("/shadow/catalog", (CatalogShadowRequest? body, ShadowRunner runner,
            AuthorizationDecisionProviderFactory factory, PdpDecisionService active) =>
        {
            if (body is null || string.IsNullOrWhiteSpace(body.Shadow))
            {
                return Results.Problem(
                    "A catalog-shadow body with a 'shadow' engine is required.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var primary = string.IsNullOrWhiteSpace(body.Primary) ? active.ProviderName : body.Primary.Trim();
            var shadow = body.Shadow.Trim();

            foreach (var name in new[] { primary, shadow })
            {
                if (!factory.TryGetProvider(name, out _))
                {
                    return Results.Problem(UnknownEngine(name, factory),
                        statusCode: StatusCodes.Status400BadRequest);
                }
            }

            return Results.Ok(runner.RunCatalog(primary, shadow, FintechScenarioCatalog.Scenarios));
        });

        // Policy version + drift: the golden snapshot's content hash (the policy version id) plus a
        // live drift check of the active engine against the golden baseline. Empty drift = the
        // enforced engine still matches the reviewed baseline.
        group.MapGet("/policy/version", (PdpDecisionService active,
            AuthorizationDecisionProviderFactory factory) =>
        {
            var provider = factory.GetProvider(active.ProviderName);
            var drift = GoldenDecisionSnapshot.Diff(
                GoldenDecisionSnapshot.Golden,
                GoldenDecisionSnapshot.Compute(provider, FintechScenarioCatalog.Scenarios));

            return Results.Ok(new
            {
                version = GoldenDecisionSnapshot.Version,
                scenarios = GoldenDecisionSnapshot.Golden.Count,
                engine = active.ProviderName,
                hasDrift = drift.Count > 0,
                drift,
            });
        });

        return app;
    }

    private static string UnknownEngine(string name, AuthorizationDecisionProviderFactory factory) =>
        $"Unknown engine '{name}'. Available engines: " +
        $"[{string.Join(", ", factory.ProviderNames.OrderBy(n => n, StringComparer.Ordinal))}].";
}
