using AuthzEntitlements.Governance.Service.Contracts;
using AuthzEntitlements.Governance.Service.Data;
using AuthzEntitlements.Governance.Service.Domain;
using AuthzEntitlements.Governance.Service.Metering;
using AuthzEntitlements.Governance.Service.Sod;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuthzEntitlements.Governance.Service.Endpoints;

// The access-governance API: access packages, JIT grant requests with a maker-checker +
// SoD approval workflow, time-bound grants whose expiry is enforced at read time, and
// access-review campaigns. Every governance decision emits an audit-ready event and a
// metric. Endpoints are anonymous: this service is called intra-cluster; edge/token
// concerns are handled in other CSs.
public static class GovernanceEndpoints
{
    // Sentinel principal id for genuinely campaign-scoped audit events: a campaign run spans
    // every active grant in the tenant, so there is no single subject principal. An explicit,
    // documented token keeps downstream partitioning (which keys on tenant + principal) from
    // seeing an empty string.
    private const string CampaignScopePrincipal = "*campaign-scope*";

    // Fallback tenant for the vanishingly rare case where a review item's owning campaign
    // cannot be loaded (an integrity violation — the campaign FK is required). Prefer the
    // real campaign/grant tenant; never emit an empty tenant, which would break partitioning.
    private const string UnknownTenant = "*unknown-tenant*";

    public static IEndpointRouteBuilder MapGovernanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/governance");

        group.MapGet("/access-packages", GetAccessPackagesAsync);
        group.MapGet("/access-packages/{code}", GetAccessPackageAsync);

        group.MapPost("/requests", CreateRequestAsync);
        group.MapGet("/requests", ListRequestsAsync);
        group.MapGet("/requests/{id:guid}", GetRequestAsync);
        group.MapPost("/requests/{id:guid}/approve", ApproveRequestAsync);
        group.MapPost("/requests/{id:guid}/reject", RejectRequestAsync);

        group.MapGet("/principals/{id}/grants", GetPrincipalGrantsAsync);
        group.MapGet("/principals/{id}/access", GetPrincipalAccessAsync);
        group.MapPost("/grants/{id:guid}/revoke", RevokeGrantAsync);

        group.MapPost("/review-campaigns", CreateCampaignAsync);
        group.MapGet("/review-campaigns", ListCampaignsAsync);
        group.MapGet("/review-campaigns/{id:guid}", GetCampaignAsync);
        group.MapPost("/review-campaigns/{id:guid}/run", RunCampaignAsync);
        group.MapPost("/review-items/{id:guid}/decision", DecideReviewItemAsync);

        return app;
    }

    // ---- Access packages ----

    private static async Task<IResult> GetAccessPackagesAsync(GovernanceDbContext db, CancellationToken ct)
    {
        var packages = await db.AccessPackages.AsNoTracking()
            .Include(p => p.Roles)
            .OrderBy(p => p.Code)
            .ToListAsync(ct);

        return TypedResults.Ok(packages.Select(ToPackageResponse).ToArray());
    }

    private static async Task<IResult> GetAccessPackageAsync(
        string code, GovernanceDbContext db, CancellationToken ct)
    {
        var package = await db.AccessPackages.AsNoTracking()
            .Include(p => p.Roles)
            .FirstOrDefaultAsync(p => p.Code == code, ct);

        return package is null ? TypedResults.NotFound() : TypedResults.Ok(ToPackageResponse(package));
    }

    // ---- Requests ----

    private static async Task<IResult> CreateRequestAsync(
        CreateAccessRequestBody? body,
        GovernanceDbContext db,
        IGovernanceAuditSink audit,
        GovernanceMetrics metrics,
        CancellationToken ct)
    {
        if (body is null
            || string.IsNullOrWhiteSpace(body.PrincipalId)
            || string.IsNullOrWhiteSpace(body.AccessPackageCode))
        {
            return Problem("principalId and accessPackageCode are required", StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(body.Justification))
        {
            return Problem("a justification is required", StatusCodes.Status400BadRequest);
        }

        var principal = await db.Principals.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == body.PrincipalId, ct);
        if (principal is null)
        {
            return Problem($"unknown principal '{body.PrincipalId}'", StatusCodes.Status404NotFound);
        }

        var package = await db.AccessPackages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == body.AccessPackageCode, ct);
        if (package is null)
        {
            return Problem($"unknown access package '{body.AccessPackageCode}'", StatusCodes.Status404NotFound);
        }

        var request = new AccessGrantRequest
        {
            Id = Guid.NewGuid(),
            PrincipalId = principal.Id,
            TenantCode = principal.TenantCode,
            AccessPackageCode = package.Code,
            Justification = body.Justification,
            RequestedDurationMinutes = body.RequestedDurationMinutes,
            Status = RequestStatus.Pending,
            SodOutcome = SodOutcome.NotEvaluated,
            RequestedAt = DateTimeOffset.UtcNow,
        };

        db.AccessGrantRequests.Add(request);
        await db.SaveChangesAsync(ct);

        metrics.RecordRequest();
        EmitDecision(audit, metrics, request.TenantCode, request.PrincipalId, GovernanceDecisionType.Request,
            request.AccessPackageCode, GovernanceOutcome.Pending, reason: null, correlationId: request.Id.ToString());

        return TypedResults.Created($"/api/governance/requests/{request.Id}", ToRequestResponse(request));
    }

    private static async Task<IResult> ListRequestsAsync(GovernanceDbContext db, CancellationToken ct)
    {
        var requests = await db.AccessGrantRequests.AsNoTracking()
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync(ct);

        return TypedResults.Ok(requests.Select(ToRequestResponse).ToArray());
    }

    private static async Task<IResult> GetRequestAsync(Guid id, GovernanceDbContext db, CancellationToken ct)
    {
        var request = await db.AccessGrantRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        return request is null ? TypedResults.NotFound() : TypedResults.Ok(ToRequestResponse(request));
    }

    private static async Task<IResult> ApproveRequestAsync(
        Guid id,
        ApproveRequestBody? body,
        GovernanceDbContext db,
        AccessApprovalService approval,
        IGovernanceAuditSink audit,
        GovernanceMetrics metrics,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.ApproverId))
        {
            return Problem("approverId is required", StatusCodes.Status400BadRequest);
        }

        var request = await db.AccessGrantRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
        {
            return TypedResults.NotFound();
        }

        if (request.Status != RequestStatus.Pending)
        {
            return Problem($"request is {request.Status} and can no longer be decided",
                StatusCodes.Status409Conflict);
        }

        var principal = await db.Principals.AsNoTracking().Include(p => p.BaselineRoles)
            .FirstOrDefaultAsync(p => p.Id == request.PrincipalId, ct);
        var package = await db.AccessPackages.AsNoTracking().Include(p => p.Roles)
            .FirstOrDefaultAsync(p => p.Code == request.AccessPackageCode, ct);
        if (principal is null || package is null)
        {
            return Problem("the referenced principal or access package no longer exists",
                StatusCodes.Status409Conflict);
        }

        // Resolve the claimed approver from the trusted governance directory rather than
        // taking the body field on faith: only a known, checker-eligible principal may sign
        // off (enforced in AccessApprovalService). An unknown/spoofed id resolves to null and
        // fails the eligibility gate.
        var approver = await db.Principals.AsNoTracking().Include(p => p.BaselineRoles)
            .FirstOrDefaultAsync(p => p.Id == body.ApproverId, ct);

        var now = DateTimeOffset.UtcNow;
        var outcome = await approval.EvaluateAsync(request, principal, package, body.ApproverId, approver, now, ct);

        switch (outcome.Disposition)
        {
            case ApprovalDisposition.MakerCheckerDenied:
                // Segregation of duties on the approval action: the requester cannot
                // approve their own elevation. No state change; the request stays Pending.
                EmitDecision(audit, metrics, request.TenantCode, request.PrincipalId,
                    GovernanceDecisionType.Approval, request.AccessPackageCode,
                    GovernanceOutcome.MakerCheckerDenied, outcome.ReasonCode, request.Id.ToString());
                return Problem(outcome.Message, StatusCodes.Status403Forbidden);

            case ApprovalDisposition.ApproverNotEligible:
                // The claimed approver is not a known checker-eligible principal (an unknown/
                // spoofed id, or a principal without an oversight role). Reject the approval
                // action; the request stays Pending so a legitimate checker can still decide.
                EmitDecision(audit, metrics, request.TenantCode, request.PrincipalId,
                    GovernanceDecisionType.Approval, request.AccessPackageCode,
                    GovernanceOutcome.ApproverNotEligible, outcome.ReasonCode, request.Id.ToString());
                return Problem(outcome.Message, StatusCodes.Status403Forbidden);

            case ApprovalDisposition.SodUnavailable:
                // The PDP could not be consulted. Fail closed: nothing is granted and the request
                // stays Pending (retryable), returning a transient 503 (not a business decision).
                // Record SodOutcome=Unavailable + reason (DecidedBy/At stay null — no decision was
                // made) so /requests distinguishes "not yet evaluated" from "evaluation attempted,
                // PDP unavailable"; a later retry overwrites it with Permit/Deny.
                request.SodOutcome = SodOutcome.Unavailable;
                request.SodReason = outcome.ReasonCode;
                if (await TrySaveDecisionAsync(db, ct) is { } unavailableConflict)
                {
                    return unavailableConflict;
                }

                EmitDecision(audit, metrics, request.TenantCode, request.PrincipalId,
                    GovernanceDecisionType.Approval, request.AccessPackageCode,
                    GovernanceOutcome.Unavailable, outcome.ReasonCode, request.Id.ToString());
                return Problem("the PDP is unavailable; retry the approval",
                    StatusCodes.Status503ServiceUnavailable);

            case ApprovalDisposition.SodDenied:
                request.Status = RequestStatus.Rejected;
                request.SodOutcome = SodOutcome.Deny;
                request.SodReason = outcome.ReasonCode;
                request.DecidedBy = body.ApproverId;
                request.DecidedAt = now;
                if (await TrySaveDecisionAsync(db, ct) is { } denyConflict)
                {
                    return denyConflict;
                }

                EmitDecision(audit, metrics, request.TenantCode, request.PrincipalId,
                    GovernanceDecisionType.Approval, request.AccessPackageCode,
                    GovernanceOutcome.SodDeny, outcome.ReasonCode, request.Id.ToString());
                return Problem($"segregation-of-duties conflict: {outcome.Message}",
                    StatusCodes.Status409Conflict);

            case ApprovalDisposition.Approved:
            default:
                var grant = outcome.Grant!;
                request.Status = RequestStatus.Approved;
                request.SodOutcome = SodOutcome.Permit;
                request.SodReason = null;
                request.DecidedBy = body.ApproverId;
                request.DecidedAt = now;
                db.AccessGrants.Add(grant);
                if (await TrySaveDecisionAsync(db, ct) is { } approveConflict)
                {
                    return approveConflict;
                }

                EmitDecision(audit, metrics, request.TenantCode, request.PrincipalId,
                    GovernanceDecisionType.Approval, request.AccessPackageCode,
                    GovernanceOutcome.Approved, reason: null, request.Id.ToString());
                metrics.RecordGrantIssued();
                EmitDecision(audit, metrics, grant.TenantCode, grant.PrincipalId,
                    GovernanceDecisionType.Grant, grant.AccessPackageCode,
                    GovernanceOutcome.GrantIssued, reason: null, grant.Id.ToString());
                return TypedResults.Ok(ToGrantResponse(grant, now));
        }
    }

    private static async Task<IResult> RejectRequestAsync(
        Guid id,
        RejectRequestBody? body,
        GovernanceDbContext db,
        IGovernanceAuditSink audit,
        GovernanceMetrics metrics,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.ApproverId))
        {
            return Problem("approverId is required", StatusCodes.Status400BadRequest);
        }

        var request = await db.AccessGrantRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null)
        {
            return TypedResults.NotFound();
        }

        if (request.Status != RequestStatus.Pending)
        {
            return Problem($"request is {request.Status} and can no longer be decided",
                StatusCodes.Status409Conflict);
        }

        // A reject is a checker action too: the rejector must be a known checker-eligible
        // principal that differs from the requester — the same gate as approve, so a reject
        // cannot be spoofed by an unknown or ineligible id either.
        var rejector = await db.Principals.AsNoTracking().Include(p => p.BaselineRoles)
            .FirstOrDefaultAsync(p => p.Id == body.ApproverId, ct);
        if (ValidateChecker(request.PrincipalId, body.ApproverId, rejector) is { } checkerError)
        {
            EmitDecision(audit, metrics, request.TenantCode, request.PrincipalId,
                GovernanceDecisionType.Rejection, request.AccessPackageCode,
                checkerError.Outcome, checkerError.ReasonCode, request.Id.ToString());
            return Problem(checkerError.Message, StatusCodes.Status403Forbidden);
        }

        var now = DateTimeOffset.UtcNow;
        request.Status = RequestStatus.Rejected;
        request.DecidedBy = body.ApproverId;
        request.DecidedAt = now;
        if (await TrySaveDecisionAsync(db, ct) is { } conflict)
        {
            return conflict;
        }

        var reason = string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason;
        EmitDecision(audit, metrics, request.TenantCode, request.PrincipalId,
            GovernanceDecisionType.Rejection, request.AccessPackageCode,
            GovernanceOutcome.Rejected, reason, request.Id.ToString());
        return TypedResults.Ok(ToRequestResponse(request));
    }

    // ---- Grants ----

    private static async Task<IResult> GetPrincipalGrantsAsync(
        string id, bool? activeOnly, GovernanceDbContext db, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var query = db.AccessGrants.AsNoTracking().Include(g => g.Roles)
            .Where(g => g.PrincipalId == id);

        // Expiry is enforced at read time — no background sweeper. When only active grants are
        // requested, push the IsActive predicate (RevokedAt == null && now < ExpiresAt) into the
        // query so expired/revoked rows (and their roles) are never materialised.
        if (activeOnly == true)
        {
            query = query.Where(g => g.RevokedAt == null && g.ExpiresAt > now);
        }

        var grants = await query.OrderByDescending(g => g.GrantedAt).ToListAsync(ct);

        var response = grants.Select(g => ToGrantResponse(g, now)).ToArray();
        return TypedResults.Ok(response);
    }

    private static async Task<IResult> GetPrincipalAccessAsync(
        string id, GovernanceDbContext db, CancellationToken ct)
    {
        var principal = await db.Principals.AsNoTracking().Include(p => p.BaselineRoles)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (principal is null)
        {
            return TypedResults.NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        // Only currently-active grants contribute to effective access. Push the IsActive predicate
        // (RevokedAt == null && now < ExpiresAt) into the query so expired/revoked grants and their
        // roles are never materialised.
        var activeGrants = await db.AccessGrants.AsNoTracking().Include(g => g.Roles)
            .Where(g => g.PrincipalId == id && g.RevokedAt == null && g.ExpiresAt > now)
            .ToListAsync(ct);

        var baseline = principal.BaselineRoles
            .Select(r => r.RoleName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToArray();

        // Effective roles = baseline UNION the roles from every currently-active grant.
        var effective = ProposedRoleSet.Compute(
            baseline, activeGrants.SelectMany(g => g.Roles.Select(r => r.RoleName))).ToArray();

        var packages = activeGrants
            .Select(g => g.AccessPackageCode)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToArray();

        return TypedResults.Ok(new PrincipalAccessResponse(
            principal.Id, principal.TenantCode, effective, baseline, packages));
    }

    private static async Task<IResult> RevokeGrantAsync(
        Guid id,
        RevokeGrantBody? body,
        GovernanceDbContext db,
        IGovernanceAuditSink audit,
        GovernanceMetrics metrics,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.RevokedBy))
        {
            return Problem("revokedBy is required", StatusCodes.Status400BadRequest);
        }

        var grant = await db.AccessGrants.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (grant is null)
        {
            return TypedResults.NotFound();
        }

        // Revoking an already-revoked grant is a conflict: the caller is acting on stale
        // state, so surface a 409 rather than silently re-stamping the revocation.
        if (grant.RevokedAt is not null)
        {
            return Problem("grant is already revoked", StatusCodes.Status409Conflict);
        }

        var now = DateTimeOffset.UtcNow;
        grant.RevokedAt = now;
        grant.RevokedBy = body.RevokedBy;
        await db.SaveChangesAsync(ct);

        metrics.RecordGrantRevoked();
        EmitDecision(audit, metrics, grant.TenantCode, grant.PrincipalId,
            GovernanceDecisionType.Grant, grant.AccessPackageCode,
            GovernanceOutcome.GrantRevoked, reason: null, grant.Id.ToString());
        return TypedResults.Ok(ToGrantResponse(grant, now));
    }

    // ---- Review campaigns ----

    private static async Task<IResult> CreateCampaignAsync(
        CreateCampaignBody? body, GovernanceDbContext db, CancellationToken ct)
    {
        if (body is null
            || string.IsNullOrWhiteSpace(body.Name)
            || string.IsNullOrWhiteSpace(body.TenantCode))
        {
            return Problem("name and tenantCode are required", StatusCodes.Status400BadRequest);
        }

        var campaign = new AccessReviewCampaign
        {
            Id = Guid.NewGuid(),
            Name = body.Name,
            TenantCode = body.TenantCode,
            CreatedAt = DateTimeOffset.UtcNow,
            DueAt = body.DueAt,
            Status = CampaignStatus.Open,
        };

        db.AccessReviewCampaigns.Add(campaign);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/api/governance/review-campaigns/{campaign.Id}", ToCampaignResponse(campaign));
    }

    private static async Task<IResult> ListCampaignsAsync(GovernanceDbContext db, CancellationToken ct)
    {
        var campaigns = await db.AccessReviewCampaigns.AsNoTracking().Include(c => c.Items)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        return TypedResults.Ok(campaigns.Select(ToCampaignResponse).ToArray());
    }

    private static async Task<IResult> GetCampaignAsync(Guid id, GovernanceDbContext db, CancellationToken ct)
    {
        var campaign = await db.AccessReviewCampaigns.AsNoTracking().Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        return campaign is null ? TypedResults.NotFound() : TypedResults.Ok(ToCampaignResponse(campaign));
    }

    private static async Task<IResult> RunCampaignAsync(
        Guid id,
        GovernanceDbContext db,
        IGovernanceAuditSink audit,
        GovernanceMetrics metrics,
        CancellationToken ct)
    {
        var campaign = await db.AccessReviewCampaigns.Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (campaign is null)
        {
            return TypedResults.NotFound();
        }

        if (campaign.Status != CampaignStatus.Open)
        {
            return Problem("campaign is not open", StatusCodes.Status409Conflict);
        }

        if (ReviewCampaignPlanner.AlreadyRun(campaign))
        {
            return Problem("campaign has already been run", StatusCodes.Status409Conflict);
        }

        var now = DateTimeOffset.UtcNow;
        // Only currently-active grants receive a review item — push the IsActive predicate
        // (RevokedAt == null && now < ExpiresAt) into the query so expired/revoked grants are never
        // materialised. The pure planner re-checks IsActive defensively, so passing only active
        // grants keeps the result identical.
        var grants = await db.AccessGrants.AsNoTracking()
            .Where(g => g.TenantCode == campaign.TenantCode
                        && g.RevokedAt == null && g.ExpiresAt > now)
            .ToListAsync(ct);

        var items = ReviewCampaignPlanner.BuildItems(campaign, grants, now);
        foreach (var item in items)
        {
            campaign.Items.Add(item);
        }

        // The Items.Count guard above is only a fast-path: two concurrent runs can both observe
        // zero items. The unique {CampaignId, AccessGrantId} index is the durable backstop — the
        // loser's insert violates it and SaveChanges throws a unique-violation (SQLSTATE 23505),
        // which we surface as 409 (not a 500). Any OTHER persistence failure (outage, timeout,
        // unrelated constraint) propagates unchanged so a real fault is never masked as "already
        // generated". The audit/metric below run only for the run that actually won.
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return Problem("campaign items are already being generated", StatusCodes.Status409Conflict);
        }

        metrics.RecordReviewRun();
        // Campaign-scoped event: a run has no single subject principal (it spans every active
        // grant in the tenant), so record the campaign tenant with the campaign-scope sentinel
        // principal rather than an empty string.
        EmitDecision(audit, metrics, campaign.TenantCode, CampaignScopePrincipal,
            GovernanceDecisionType.Campaign, campaign.Id.ToString(),
            GovernanceOutcome.CampaignRun, reason: null, campaign.Id.ToString());
        return TypedResults.Ok(new CampaignRunResponse(campaign.Id, items.Count));
    }

    private static async Task<IResult> DecideReviewItemAsync(
        Guid id,
        ReviewItemDecisionBody? body,
        GovernanceDbContext db,
        IGovernanceAuditSink audit,
        GovernanceMetrics metrics,
        CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.ReviewedBy))
        {
            return Problem("reviewedBy is required", StatusCodes.Status400BadRequest);
        }

        if (!TryParseReviewDecision(body.Decision, out var decision))
        {
            return Problem("decision must be 'Certify' or 'Revoke'", StatusCodes.Status400BadRequest);
        }

        var item = await db.AccessReviewItems.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null)
        {
            return TypedResults.NotFound();
        }

        if (item.Decision != ReviewDecision.Pending)
        {
            return Problem($"review item is already {item.Decision}", StatusCodes.Status409Conflict);
        }

        // Load the owning campaign up front (tracked): its TenantCode is the review item's
        // tenant — all items in a campaign share it — needed to partition the audit event
        // below, and it is the entity whose status may flip to Completed.
        var campaign = await db.AccessReviewCampaigns.FirstOrDefaultAsync(c => c.Id == item.CampaignId, ct);

        var now = DateTimeOffset.UtcNow;

        // Load the linked grant only when a Revoke could act on it. A Revoke whose grant no
        // longer exists (a data-integrity case) must not mark the item decided: doing so would
        // leave an audit trail claiming a revocation that never happened. Reject it with a 409
        // before mutating anything, leaving the item Pending. Certify never touches a grant.
        var linkedGrant = decision == ReviewDecision.Revoke
            ? await db.AccessGrants.FirstOrDefaultAsync(g => g.Id == item.AccessGrantId, ct)
            : null;

        if (decision == ReviewDecision.Revoke && linkedGrant is null)
        {
            return Problem("the grant linked to this review item no longer exists; cannot revoke",
                StatusCodes.Status409Conflict);
        }

        // The pure planner mutates the item (and the grant on Revoke) and reports whether it
        // revoked, so the audit event/metric is emitted exactly once.
        var grantRevoked = ReviewCampaignPlanner.ApplyDecision(item, decision, body.ReviewedBy, linkedGrant, now);

        // Persist this item's decision (and any grant revocation) BEFORE checking whether the
        // campaign is complete. Counting pending items before this save races under concurrency:
        // two parallel decisions of the last two pending items would each still read the other as
        // Pending and neither would complete the campaign, leaving it stuck Open (Copilot review).
        await db.SaveChangesAsync(ct);

        if (grantRevoked && linkedGrant is not null)
        {
            metrics.RecordGrantRevoked();
            EmitDecision(audit, metrics, linkedGrant.TenantCode, linkedGrant.PrincipalId,
                GovernanceDecisionType.Grant, linkedGrant.AccessPackageCode,
                GovernanceOutcome.GrantRevoked, "revoked by access review", linkedGrant.Id.ToString());
        }

        // Now that this item's decision is durable, complete the campaign when NO item remains
        // Pending — the count INCLUDES the current item (now persisted as decided), so whichever
        // concurrent decision commits last observes zero pending and completes the campaign;
        // re-completing an already-Completed campaign is a harmless idempotent no-op.
        if (campaign is not null && campaign.Status != CampaignStatus.Completed)
        {
            var pendingRemaining = await db.AccessReviewItems
                .CountAsync(i => i.CampaignId == item.CampaignId
                                 && i.Decision == ReviewDecision.Pending, ct);
            if (pendingRemaining == 0)
            {
                campaign.Status = CampaignStatus.Completed;
                await db.SaveChangesAsync(ct);
            }
        }

        // Partition the review event by the item's real tenant + principal. Prefer the
        // campaign tenant (authoritative), fall back to the revoked grant's tenant, and only
        // then a documented sentinel — never an empty string.
        var reviewTenant = campaign?.TenantCode ?? linkedGrant?.TenantCode ?? UnknownTenant;
        EmitDecision(audit, metrics, reviewTenant, item.PrincipalId,
            GovernanceDecisionType.Review, item.Id.ToString(),
            GovernanceOutcome.ReviewDecided, decision.ToString().ToLowerInvariant(), item.CampaignId.ToString());

        return TypedResults.Ok(ToReviewItemResponse(item));
    }

    // ---- Shared helpers ----

    // Saves a decide-once mutation (approve/reject). A concurrent second decision on the
    // same request loses the xmin optimistic-concurrency check; surface that as a 409
    // rather than letting last-writer-win. Returns null on success, or the 409 result.
    private static async Task<IResult?> TrySaveDecisionAsync(GovernanceDbContext db, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return Problem("the request was decided concurrently; reload and retry",
                StatusCodes.Status409Conflict);
        }
    }

    private static void EmitDecision(
        IGovernanceAuditSink audit,
        GovernanceMetrics metrics,
        string tenantCode,
        string principalId,
        GovernanceDecisionType type,
        string target,
        GovernanceOutcome outcome,
        string? reason,
        string? correlationId)
    {
        audit.Record(new GovernanceDecision(
            tenantCode, principalId, type, target, outcome, reason, correlationId, DateTimeOffset.UtcNow));
        metrics.RecordDecision(GovernanceWire.Token(type), GovernanceWire.Token(outcome));
    }

    private static IResult Problem(string detail, int statusCode) =>
        TypedResults.Problem(detail, statusCode: statusCode);

    // Validates a checker action (approve/reject): the checker must differ from the requester
    // AND be a known, checker-eligible principal (an oversight role). Returns null when the
    // checker is valid, otherwise the stable audit CODE, the human message, and the audit
    // outcome to record. Keeps the reject path's gate identical to the approve path (which
    // enforces the same two rules inside AccessApprovalService); the rule itself lives once
    // in GovernanceRules.
    private static (GovernanceOutcome Outcome, string ReasonCode, string Message)? ValidateChecker(
        string requesterId, string checkerId, Principal? checker)
    {
        if (!GovernanceRules.CheckerDiffersFromRequester(requesterId, checkerId))
        {
            return (GovernanceOutcome.MakerCheckerDenied,
                GovernanceRules.MakerEqualsCheckerCode, GovernanceRules.MakerEqualsCheckerMessage);
        }

        if (checker is null
            || !GovernanceRules.IsCheckerEligible(checker.BaselineRoles.Select(r => r.RoleName)))
        {
            return (GovernanceOutcome.ApproverNotEligible,
                GovernanceRules.ApproverNotEligibleCode, GovernanceRules.ApproverNotEligibleMessage);
        }

        return null;
    }

    private static bool TryParseReviewDecision(string? value, out ReviewDecision decision)
    {
        if (Enum.TryParse(value, ignoreCase: true, out decision)
            && decision is ReviewDecision.Certify or ReviewDecision.Revoke)
        {
            return true;
        }

        decision = ReviewDecision.Pending;
        return false;
    }

    private static AccessPackageResponse ToPackageResponse(AccessPackage package) =>
        new(package.Code, package.DisplayName, package.Description,
            package.DefaultDurationMinutes, package.RequiresApproval,
            package.Roles.Select(r => r.RoleName).OrderBy(r => r, StringComparer.Ordinal).ToArray());

    private static AccessRequestResponse ToRequestResponse(AccessGrantRequest request) =>
        new(request.Id, request.PrincipalId, request.TenantCode, request.AccessPackageCode,
            request.Justification, request.RequestedDurationMinutes,
            request.Status.ToString(), request.SodOutcome.ToString(), request.SodReason,
            request.RequestedAt, request.DecidedBy, request.DecidedAt);

    private static AccessGrantResponse ToGrantResponse(AccessGrant grant, DateTimeOffset now) =>
        new(grant.Id, grant.RequestId, grant.PrincipalId, grant.TenantCode, grant.AccessPackageCode,
            grant.Roles.Select(r => r.RoleName).OrderBy(r => r, StringComparer.Ordinal).ToArray(),
            grant.GrantedAt, grant.ExpiresAt, grant.RevokedAt, grant.RevokedBy,
            grant.IsActive(now), GrantStatus(grant, now));

    private static string GrantStatus(AccessGrant grant, DateTimeOffset now) =>
        grant.RevokedAt is not null ? "revoked" : now < grant.ExpiresAt ? "active" : "expired";

    private static ReviewItemResponse ToReviewItemResponse(AccessReviewItem item) =>
        new(item.Id, item.CampaignId, item.AccessGrantId, item.PrincipalId,
            item.Decision.ToString(), item.ReviewedBy, item.ReviewedAt);

    private static ReviewCampaignResponse ToCampaignResponse(AccessReviewCampaign campaign) =>
        new(campaign.Id, campaign.Name, campaign.TenantCode, campaign.CreatedAt, campaign.DueAt,
            campaign.Status.ToString(),
            campaign.Items.OrderBy(i => i.PrincipalId, StringComparer.Ordinal)
                .Select(ToReviewItemResponse).ToArray());
}
