using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using OpenFga.Sdk.Exceptions;

namespace AuthzEntitlements.Authz.Pdp.Endpoints;

// The OpenFGA (ReBAC) surface, grouped under /api/authz/rebac. These sit alongside the
// AuthZEN evaluate endpoints and expose the relationship-native queries the sync decision
// contract can't: a scenario self-check and the two reverse-index directions. All three talk
// to a live OpenFGA server (the "openfga" container running + Pdp:OpenFga:ApiUrl injected).
// Failure modes (this host adds no exception-handling middleware): bad query input → 400;
// OpenFGA unavailable — not configured (blank ApiUrl → InvalidOperationException) OR the SDK
// failing to reach the server (ApiException / HttpRequestException) → 503 ProblemDetails (see
// IsOpenFgaUnavailable). Anonymous, consistent with the CS05 in-process reference host.
public static class RebacEndpoints
{
    public static IEndpointRouteBuilder MapRebacEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/authz/rebac");

        // Bootstraps the store/model/tuples, then runs the ReBAC scenario catalog (forward Checks +
        // both reverse-index directions) against live OpenFGA; 200 when all pass, 500 otherwise, or
        // 503 when OpenFGA is not configured/reachable.
        group.MapPost("/verify", async (OpenFgaRebacService fga) =>
        {
            try
            {
                await fga.EnsureBootstrappedAsync();
            }
            catch (Exception ex) when (IsOpenFgaUnavailable(ex))
            {
                // OpenFGA unavailable (not configured, or the server unreachable during bootstrap) —
                // an actionable 503 rather than a raw 500. Bootstrap is the first OpenFGA call, so a
                // connection failure surfaces here before the scenario loops run.
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var results = new List<object>();
            var allPassed = true;
            var passedCount = 0;

            foreach (var s in RebacScenarioCatalog.Forward)
            {
                var allowed = await fga.CheckAsync(
                    $"{RebacTypes.User}:{s.UserId}", s.Relation, $"{s.ObjectType}:{s.ObjectId}");
                var passed = allowed == s.ExpectAllowed;
                allPassed &= passed;
                if (passed) { passedCount++; }
                results.Add(new
                {
                    kind = "forward",
                    s.Id,
                    s.Description,
                    expected = s.ExpectAllowed,
                    actual = allowed,
                    passed,
                });
            }

            foreach (var s in RebacScenarioCatalog.WhoCanAccess)
            {
                var users = await fga.WhoCanAccessAsync(s.ObjectType, s.ObjectId, s.Relation);
                // Exact-set oracle: the returned users must match ExpectedUserIds with no missing AND
                // no extra, so an engine that over-grants (leaks a viewer) is caught, not just one
                // that under-grants.
                var missing = s.ExpectedUserIds.Where(u => !users.Contains(u)).ToList();
                var unexpected = users.Where(u => !s.ExpectedUserIds.Contains(u)).ToList();
                var passed = missing.Count == 0 && unexpected.Count == 0;
                allPassed &= passed;
                if (passed) { passedCount++; }
                results.Add(new
                {
                    kind = "who-can-access",
                    s.Id,
                    s.Description,
                    expectedExactly = s.ExpectedUserIds,
                    actual = users,
                    missing,
                    unexpected,
                    passed,
                });
            }

            foreach (var s in RebacScenarioCatalog.WhatCanUserAccess)
            {
                var objects = await fga.WhatCanUserAccessAsync(s.UserId, s.Relation, s.ObjectType);
                var missing = s.ExpectedObjectIds.Where(o => !objects.Contains(o)).ToList();
                var leaked = s.ExcludedObjectIds.Where(objects.Contains).ToList();
                var passed = missing.Count == 0 && leaked.Count == 0;
                allPassed &= passed;
                if (passed) { passedCount++; }
                results.Add(new
                {
                    kind = "what-can-user-access",
                    s.Id,
                    s.Description,
                    expectedSupersetOf = s.ExpectedObjectIds,
                    expectedToExclude = s.ExcludedObjectIds,
                    actual = objects,
                    missing,
                    leaked,
                    passed,
                });
            }

            var body = new { allPassed, passed = passedCount, total = results.Count, results };
            return allPassed
                ? Results.Ok(body)
                : Results.Json(body, statusCode: StatusCodes.Status500InternalServerError);
        });

        // Reverse index — who can access an object: /who-can-access?type=account&id=acme-checking&relation=can_view
        group.MapGet("/who-can-access", async (
            string? type, string? id, string? relation, OpenFgaRebacService fga) =>
        {
            var error = ValidateReverseQuery(type, relation, id, "id");
            if (error is not null)
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var users = await fga.WhoCanAccessAsync(type!, id!, relation!);
                return Results.Ok(new { @object = $"{type}:{id}", relation, users });
            }
            catch (Exception ex) when (IsOpenFgaUnavailable(ex))
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        // Reverse index — what a user can access: /what-can-user-access?user=rm-anne&relation=can_view&type=account
        group.MapGet("/what-can-user-access", async (
            string? user, string? relation, string? type, OpenFgaRebacService fga) =>
        {
            var error = ValidateReverseQuery(type, relation, user, "user");
            if (error is not null)
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                var objects = await fga.WhatCanUserAccessAsync(user!, relation!, type!);
                return Results.Ok(new { user = $"{RebacTypes.User}:{user}", relation, type, objects });
            }
            catch (Exception ex) when (IsOpenFgaUnavailable(ex))
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        return app;
    }

    // The object types and computed relations the reverse-index endpoints answer: these queries ask
    // who/what has can_view/can_transact on an account. Anything else is rejected with a clean 400
    // (matching /api/authz/evaluate's fail-closed validation) rather than surfacing a 500 from an
    // OpenFGA query against an unmodelled type/relation or a blank object.
    private static readonly HashSet<string> QueryableTypes =
        new(StringComparer.Ordinal) { RebacTypes.Account };

    private static readonly HashSet<string> QueryableRelations =
        new(StringComparer.Ordinal) { RebacRelations.CanView, RebacRelations.CanTransact };

    // Returns a problem message for an invalid reverse-index query, or null when it is well-formed.
    // principal is the required id/user; principalName names it for the message.
    private static string? ValidateReverseQuery(string? type, string? relation, string? principal, string principalName)
    {
        if (string.IsNullOrWhiteSpace(principal))
        {
            return $"Query parameter '{principalName}' is required.";
        }

        if (principal.Contains(':'))
        {
            return $"Query parameter '{principalName}' must be a bare id with no 'type:' prefix " +
                $"— the adapter adds the type qualifier itself.";
        }

        if (string.IsNullOrWhiteSpace(type) || !QueryableTypes.Contains(type))
        {
            return $"Query parameter 'type' must be one of: " +
                $"{string.Join(", ", QueryableTypes.OrderBy(t => t, StringComparer.Ordinal))}.";
        }

        if (string.IsNullOrWhiteSpace(relation) || !QueryableRelations.Contains(relation))
        {
            return $"Query parameter 'relation' must be one of: " +
                $"{string.Join(", ", QueryableRelations.OrderBy(r => r, StringComparer.Ordinal))}.";
        }

        return null;
    }

    // OpenFGA is "unavailable" when it is not configured (blank ApiUrl → InvalidOperationException
    // from the service) or the SDK cannot reach the server (its ApiException base, or a raw
    // HttpRequestException). These map to a 503; anything else (a genuine bug) stays a 500.
    private static bool IsOpenFgaUnavailable(Exception ex) =>
        ex is InvalidOperationException or ApiException or HttpRequestException;
}
