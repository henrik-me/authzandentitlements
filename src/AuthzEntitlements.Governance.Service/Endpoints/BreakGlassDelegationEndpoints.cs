using AuthzEntitlements.Governance.Service.BreakGlass;
using AuthzEntitlements.Governance.Service.Contracts;
using AuthzEntitlements.Governance.Service.Delegation;
using AuthzEntitlements.Governance.Service.Domain;
using AuthzEntitlements.Governance.Service.Metering;

namespace AuthzEntitlements.Governance.Service.Endpoints;

// CS21 — the break-glass + delegation grant lifecycle API. Break-glass grants are bounded,
// auto-expiring emergency elevations that force a mandatory post-review; delegation grants let
// a manager delegate capability scopes to a delegate until revoked or expired. Both live in
// in-memory, time-boxed stores that mirror AccessGrant.IsActive(now) — expiry is enforced at
// read time, no background sweeper. Every state change emits an audit-ready GovernanceDecision
// event and a GovernanceMetrics counter, exactly like GovernanceEndpoints.
//
// These endpoints are ANONYMOUS by design, consistent with the other intra-cluster governance
// reads/writes (grants, principals, review campaigns). Only the CS29 access-request endpoints
// are token-gated; the PDP and the Bank.Web wiring call these grant endpoints intra-cluster.
public static class BreakGlassDelegationEndpoints
{
    public static IEndpointRouteBuilder MapBreakGlassDelegationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/governance");

        group.MapPost("/break-glass", IssueBreakGlass);
        group.MapGet("/break-glass", ListBreakGlass);
        // The literal "pending-review" cannot collide with the {id:guid} route below (it is not
        // a Guid), so ordering is not load-bearing — listed first only for readability.
        group.MapGet("/break-glass/pending-review", ListBreakGlassPendingReview);
        group.MapGet("/break-glass/{id:guid}", GetBreakGlass);
        group.MapPost("/break-glass/{id:guid}/review", ReviewBreakGlass);

        group.MapPost("/delegations", CreateDelegation);
        group.MapGet("/delegations", ListDelegations);
        group.MapGet("/delegations/{id:guid}", GetDelegation);
        group.MapPost("/delegations/{id:guid}/revoke", RevokeDelegation);

        return app;
    }

    // ---- Break-glass ----

    private static IResult IssueBreakGlass(
        IssueBreakGlassRequest? body,
        BreakGlassGrantStore store,
        IGovernanceAuditSink audit,
        GovernanceMetrics metrics)
    {
        if (body is null
            || string.IsNullOrWhiteSpace(body.PrincipalId)
            || string.IsNullOrWhiteSpace(body.TenantCode)
            || string.IsNullOrWhiteSpace(body.Action)
            || string.IsNullOrWhiteSpace(body.Justification))
        {
            return Problem(
                "principalId, tenantCode, action and justification are required",
                StatusCodes.Status400BadRequest);
        }

        if (body.DurationMinutes <= 0)
        {
            return Problem("durationMinutes must be a positive number of minutes",
                StatusCodes.Status400BadRequest);
        }

        var now = DateTimeOffset.UtcNow;
        var grant = store.Issue(
            body.PrincipalId, body.TenantCode, body.Action, body.Justification, body.DurationMinutes, now);

        metrics.RecordGrantIssued();
        GovernanceDecisionEmitter.Emit(audit, metrics, grant.TenantCode, grant.PrincipalId,
            GovernanceDecisionType.Grant, grant.Action, GovernanceOutcome.GrantIssued,
            reason: null, grant.Id.ToString());

        return TypedResults.Created(
            $"/api/governance/break-glass/{grant.Id}", ToBreakGlassDto(grant, now));
    }

    private static IResult ListBreakGlass(bool? activeOnly, BreakGlassGrantStore store)
    {
        var now = DateTimeOffset.UtcNow;
        var grants = activeOnly == true ? store.ListActive(now) : store.ListAll();
        return TypedResults.Ok(grants.Select(g => ToBreakGlassDto(g, now)).ToArray());
    }

    private static IResult ListBreakGlassPendingReview(BreakGlassGrantStore store)
    {
        var now = DateTimeOffset.UtcNow;
        var grants = store.ListRequiringReview(now);
        return TypedResults.Ok(grants.Select(g => ToBreakGlassDto(g, now)).ToArray());
    }

    private static IResult GetBreakGlass(Guid id, BreakGlassGrantStore store)
    {
        var grant = store.Get(id);
        return grant is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ToBreakGlassDto(grant, DateTimeOffset.UtcNow));
    }

    private static IResult ReviewBreakGlass(
        Guid id,
        ReviewBreakGlassRequest? body,
        BreakGlassGrantStore store,
        IGovernanceAuditSink audit,
        GovernanceMetrics metrics)
    {
        if (body is null
            || string.IsNullOrWhiteSpace(body.ReviewedBy)
            || string.IsNullOrWhiteSpace(body.Outcome))
        {
            return Problem("reviewedBy and outcome are required", StatusCodes.Status400BadRequest);
        }

        if (store.Get(id) is null)
        {
            return TypedResults.NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        BreakGlassGrant grant;
        try
        {
            grant = store.Review(id, body.ReviewedBy, body.Outcome, now);
        }
        catch (KeyNotFoundException)
        {
            // The grant vanished between the pre-check and the review (TOCTOU); the store is the
            // authoritative fail-closed gate, so honour its verdict.
            return TypedResults.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(ex.Message, StatusCodes.Status409Conflict);
        }

        GovernanceDecisionEmitter.Emit(audit, metrics, grant.TenantCode, grant.PrincipalId,
            GovernanceDecisionType.Review, grant.Id.ToString(), GovernanceOutcome.ReviewDecided,
            grant.ReviewOutcome, grant.Id.ToString());

        return TypedResults.Ok(ToBreakGlassDto(grant, now));
    }

    // ---- Delegation ----

    private static IResult CreateDelegation(
        CreateDelegationRequest? body,
        DelegationGrantStore store,
        IGovernanceAuditSink audit,
        GovernanceMetrics metrics)
    {
        if (body is null
            || string.IsNullOrWhiteSpace(body.ManagerId)
            || string.IsNullOrWhiteSpace(body.DelegateId)
            || string.IsNullOrWhiteSpace(body.TenantCode))
        {
            return Problem("managerId, delegateId and tenantCode are required",
                StatusCodes.Status400BadRequest);
        }

        if (body.Scopes is null || body.Scopes.Count == 0)
        {
            return Problem("at least one delegated scope is required", StatusCodes.Status400BadRequest);
        }

        if (body.DurationMinutes <= 0)
        {
            return Problem("durationMinutes must be a positive number of minutes",
                StatusCodes.Status400BadRequest);
        }

        var now = DateTimeOffset.UtcNow;
        DelegationGrant grant;
        try
        {
            grant = store.Create(
                body.ManagerId, body.DelegateId, body.TenantCode, body.Scopes, body.DurationMinutes, now);
        }
        catch (ArgumentException ex)
        {
            // The store applies the remaining fail-closed rules (self-delegation, all-blank
            // scopes); surface them as a clear 400 rather than a 500.
            return Problem(ex.Message, StatusCodes.Status400BadRequest);
        }

        metrics.RecordGrantIssued();
        GovernanceDecisionEmitter.Emit(audit, metrics, grant.TenantCode, grant.ManagerId,
            GovernanceDecisionType.Grant, grant.DelegateId, GovernanceOutcome.GrantIssued,
            reason: null, grant.Id.ToString());

        return TypedResults.Created(
            $"/api/governance/delegations/{grant.Id}", ToDelegationDto(grant, now));
    }

    private static IResult ListDelegations(bool? activeOnly, DelegationGrantStore store)
    {
        var now = DateTimeOffset.UtcNow;
        var grants = activeOnly == true ? store.ListActive(now) : store.ListAll();
        return TypedResults.Ok(grants.Select(g => ToDelegationDto(g, now)).ToArray());
    }

    private static IResult GetDelegation(Guid id, DelegationGrantStore store)
    {
        var grant = store.Get(id);
        return grant is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ToDelegationDto(grant, DateTimeOffset.UtcNow));
    }

    private static IResult RevokeDelegation(
        Guid id,
        RevokeDelegationRequest? body,
        DelegationGrantStore store,
        IGovernanceAuditSink audit,
        GovernanceMetrics metrics)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.RevokedBy))
        {
            return Problem("revokedBy is required", StatusCodes.Status400BadRequest);
        }

        if (store.Get(id) is null)
        {
            return TypedResults.NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        DelegationGrant grant;
        try
        {
            grant = store.Revoke(id, body.RevokedBy, now);
        }
        catch (KeyNotFoundException)
        {
            return TypedResults.NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Problem(ex.Message, StatusCodes.Status409Conflict);
        }

        metrics.RecordGrantRevoked();
        GovernanceDecisionEmitter.Emit(audit, metrics, grant.TenantCode, grant.ManagerId,
            GovernanceDecisionType.Grant, grant.DelegateId, GovernanceOutcome.GrantRevoked,
            reason: null, grant.Id.ToString());

        return TypedResults.Ok(ToDelegationDto(grant, now));
    }

    // ---- Shared helpers ----

    private static IResult Problem(string detail, int statusCode) =>
        TypedResults.Problem(detail, statusCode: statusCode);

    private static BreakGlassGrantDto ToBreakGlassDto(BreakGlassGrant grant, DateTimeOffset now) =>
        new(grant.Id, grant.PrincipalId, grant.TenantCode, grant.Action, grant.Justification,
            grant.GrantedAt, grant.ExpiresAt, grant.ReviewedAt, grant.ReviewedBy, grant.ReviewOutcome,
            grant.IsActive(now), grant.RequiresReview(now), BreakGlassStatus(grant, now));

    private static string BreakGlassStatus(BreakGlassGrant grant, DateTimeOffset now) =>
        grant.IsActive(now) ? "active" : grant.ReviewedAt is not null ? "reviewed" : "pending-review";

    private static DelegationGrantDto ToDelegationDto(DelegationGrant grant, DateTimeOffset now) =>
        new(grant.Id, grant.ManagerId, grant.DelegateId, grant.TenantCode, grant.Scopes.ToArray(),
            grant.GrantedAt, grant.ExpiresAt, grant.RevokedAt, grant.RevokedBy,
            grant.IsActive(now), DelegationStatus(grant, now));

    private static string DelegationStatus(DelegationGrant grant, DateTimeOffset now) =>
        grant.RevokedAt is not null ? "revoked" : now < grant.ExpiresAt ? "active" : "expired";
}
