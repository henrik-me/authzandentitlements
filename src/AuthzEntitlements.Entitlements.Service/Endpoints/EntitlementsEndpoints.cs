using AuthzEntitlements.Entitlements.Service.Contracts;
using AuthzEntitlements.Entitlements.Service.Data;
using AuthzEntitlements.Entitlements.Service.Domain;
using AuthzEntitlements.Entitlements.Service.Features;
using AuthzEntitlements.Entitlements.Service.Metering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuthzEntitlements.Entitlements.Service.Endpoints;

// The commercial entitlements API. Every decision (module/feature/quota/seat) emits an
// audit-ready event and a metric. Endpoints are anonymous: this service is called
// intra-cluster by Bank.Api; edge/token concerns are handled in other CSs. Lookups are
// keyed by tenant Code. Unknown tenants fail closed with a deny/empty result and HTTP
// 200 (so the caller can fail closed itself) — except /plan, which returns 404.
public static class EntitlementsEndpoints
{
    private const string NoSubscription = "no subscription";
    private const int MaxConsumeAttempts = 8;
    private const int MaxSeatAttempts = 8;

    public static IEndpointRouteBuilder MapEntitlementsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/entitlements/{tenantCode}");

        group.MapGet("/plan", GetPlanAsync);
        group.MapGet("/modules/{moduleKey}", GetModuleAsync);
        group.MapGet("/features/{featureKey}", GetFeatureAsync);
        group.MapPost("/quotas/{quotaKey}/consume", ConsumeQuotaAsync);
        group.MapGet("/seats", GetSeatsAsync);
        group.MapPost("/seats/assign", AssignSeatAsync);
        group.MapPost("/seats/release", ReleaseSeatAsync);

        return app;
    }

    private static async Task<IResult> GetPlanAsync(
        string tenantCode, EntitlementsDbContext db, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantCode == tenantCode, ct);
        if (subscription is null)
        {
            return TypedResults.NotFound();
        }

        var plan = await db.Plans.AsNoTracking()
            .Include(p => p.Modules)
            .FirstAsync(p => p.Tier == subscription.PlanTier, ct);

        var seatsUsed = await db.SeatAssignments.AsNoTracking()
            .CountAsync(a => a.SubscriptionId == subscription.Id, ct);

        var modules = plan.Modules
            .Select(m => m.ModuleKey)
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        var features = FeatureCatalog.FeaturesFor(subscription.PlanTier).ToArray();

        return TypedResults.Ok(new PlanSummaryResponse(
            tenantCode,
            subscription.PlanTier.ToString(),
            plan.SeatLimit,
            seatsUsed,
            modules,
            features));
    }

    private static async Task<IResult> GetModuleAsync(
        string tenantCode,
        string moduleKey,
        EntitlementsDbContext db,
        IEntitlementAuditSink audit,
        EntitlementsMetrics metrics,
        CancellationToken ct)
    {
        var subscription = await db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantCode == tenantCode, ct);
        if (subscription is null)
        {
            Emit(audit, metrics, tenantCode, EntitlementDecisionType.Module, moduleKey,
                EntitlementOutcome.Deny, planTier: string.Empty);
            return TypedResults.Ok(new ModuleEntitlementResponse(false, string.Empty, NoSubscription));
        }

        var entitled = await db.PlanModules.AsNoTracking()
            .AnyAsync(m => m.PlanTier == subscription.PlanTier && m.ModuleKey == moduleKey, ct);

        var planTier = subscription.PlanTier.ToString();
        var reason = entitled ? "module licensed" : "module not in plan";
        Emit(audit, metrics, tenantCode, EntitlementDecisionType.Module, moduleKey,
            entitled ? EntitlementOutcome.Allow : EntitlementOutcome.Deny, planTier);

        return TypedResults.Ok(new ModuleEntitlementResponse(entitled, planTier, reason));
    }

    private static async Task<IResult> GetFeatureAsync(
        string tenantCode,
        string featureKey,
        EntitlementsDbContext db,
        IFeatureGate featureGate,
        IEntitlementAuditSink audit,
        EntitlementsMetrics metrics,
        CancellationToken ct)
    {
        var subscription = await db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantCode == tenantCode, ct);
        if (subscription is null)
        {
            Emit(audit, metrics, tenantCode, EntitlementDecisionType.Feature, featureKey,
                EntitlementOutcome.Deny, planTier: string.Empty);
            return TypedResults.Ok(new FeatureEntitlementResponse(false, string.Empty, NoSubscription));
        }

        var enabled = await featureGate.IsEnabledAsync(featureKey, subscription.PlanTier, ct);
        var planTier = subscription.PlanTier.ToString();
        var reason = !FeatureCatalog.IsKnown(featureKey)
            ? "unknown feature"
            : enabled ? "feature enabled" : "feature disabled";

        Emit(audit, metrics, tenantCode, EntitlementDecisionType.Feature, featureKey,
            enabled ? EntitlementOutcome.Allow : EntitlementOutcome.Deny, planTier);

        return TypedResults.Ok(new FeatureEntitlementResponse(enabled, planTier, reason));
    }

    private static async Task<IResult> ConsumeQuotaAsync(
        string tenantCode,
        string quotaKey,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ConsumeQuotaRequest? request,
        EntitlementsDbContext db,
        IEntitlementAuditSink audit,
        EntitlementsMetrics metrics,
        CancellationToken ct)
    {
        var amount = request is { Amount: > 0 } ? request.Amount : 1;

        var subscription = await db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantCode == tenantCode, ct);
        if (subscription is null)
        {
            EmitQuota(audit, metrics, tenantCode, quotaKey, EntitlementOutcome.Deny,
                planTier: string.Empty, amount, used: 0, limit: 0, consumed: 0);
            return TypedResults.Ok(new QuotaConsumeResponse(false, 0, 0, 0, NoSubscription));
        }

        var planTier = subscription.PlanTier.ToString();
        var planQuota = await db.PlanQuotas.AsNoTracking()
            .FirstOrDefaultAsync(q => q.PlanTier == subscription.PlanTier && q.QuotaKey == quotaKey, ct);
        if (planQuota is null)
        {
            EmitQuota(audit, metrics, tenantCode, quotaKey, EntitlementOutcome.Deny,
                planTier, amount, used: 0, limit: 0, consumed: 0);
            return TypedResults.Ok(new QuotaConsumeResponse(false, 0, 0, 0, "quota not in plan"));
        }

        var limit = planQuota.Limit;
        var period = UsageCounter.CurrentPeriod(DateTimeOffset.UtcNow);

        // Optimistic-concurrency retry: two concurrent consumes may read the same Used;
        // the xmin token (or the unique-index insert) makes the loser fail, so we reload
        // and re-evaluate rather than over-granting quota.
        for (var attempt = 1; ; attempt++)
        {
            var counter = await db.UsageCounters
                .FirstOrDefaultAsync(
                    u => u.TenantCode == tenantCode && u.QuotaKey == quotaKey && u.PeriodKey == period, ct);

            var used = counter?.Used ?? 0;
            var decision = QuotaDecision.Evaluate(limit, used, amount);

            if (!decision.Allowed)
            {
                EmitQuota(audit, metrics, tenantCode, quotaKey, EntitlementOutcome.Deny,
                    planTier, amount, decision.Used, decision.Limit, consumed: 0);
                return TypedResults.Ok(new QuotaConsumeResponse(
                    false, decision.Limit, decision.Used, decision.Remaining, decision.Reason));
            }

            if (counter is null)
            {
                db.UsageCounters.Add(new UsageCounter
                {
                    Id = Guid.NewGuid(),
                    TenantCode = tenantCode,
                    QuotaKey = quotaKey,
                    PeriodKey = period,
                    Used = decision.Used,
                });
            }
            else
            {
                counter.Used = decision.Used;
            }

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (attempt < MaxConsumeAttempts)
            {
                db.ChangeTracker.Clear();
                continue;
            }

            EmitQuota(audit, metrics, tenantCode, quotaKey, EntitlementOutcome.Allow,
                planTier, amount, decision.Used, decision.Limit, consumed: amount);
            return TypedResults.Ok(new QuotaConsumeResponse(
                true, decision.Limit, decision.Used, decision.Remaining, decision.Reason));
        }
    }

    private static async Task<IResult> GetSeatsAsync(
        string tenantCode,
        EntitlementsDbContext db,
        IEntitlementAuditSink audit,
        EntitlementsMetrics metrics,
        CancellationToken ct)
    {
        var subscription = await db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantCode == tenantCode, ct);
        if (subscription is null)
        {
            Emit(audit, metrics, tenantCode, EntitlementDecisionType.Seat, "seats",
                EntitlementOutcome.Deny, planTier: string.Empty);
            return TypedResults.Ok(new SeatSummaryResponse(string.Empty, 0, 0, 0));
        }

        var plan = await db.Plans.AsNoTracking().FirstAsync(p => p.Tier == subscription.PlanTier, ct);
        var seatsUsed = await db.SeatAssignments.AsNoTracking()
            .CountAsync(a => a.SubscriptionId == subscription.Id, ct);
        var remaining = SeatMath.Remaining(plan.SeatLimit, seatsUsed);

        Emit(audit, metrics, tenantCode, EntitlementDecisionType.Seat, "seats",
            SeatMath.HasCapacity(plan.SeatLimit, seatsUsed) ? EntitlementOutcome.Allow : EntitlementOutcome.Deny,
            subscription.PlanTier.ToString());

        return TypedResults.Ok(new SeatSummaryResponse(
            subscription.PlanTier.ToString(), plan.SeatLimit, seatsUsed, remaining));
    }

    private static async Task<IResult> AssignSeatAsync(
        string tenantCode,
        SeatMutationRequest request,
        EntitlementsDbContext db,
        IEntitlementAuditSink audit,
        EntitlementsMetrics metrics,
        CancellationToken ct)
    {
        var subscription = await db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantCode == tenantCode, ct);
        if (subscription is null)
        {
            Emit(audit, metrics, tenantCode, EntitlementDecisionType.Seat, "seats",
                EntitlementOutcome.Deny, planTier: string.Empty);
            return TypedResults.Ok(new SeatAssignmentResponse(false, 0, 0, 0, NoSubscription));
        }

        var plan = await db.Plans.AsNoTracking().FirstAsync(p => p.Tier == subscription.PlanTier, ct);
        var planTier = subscription.PlanTier.ToString();

        // Enforce the seat cap atomically. Two concurrent assigns could each read the same
        // seatsUsed and both grant a seat past the limit; a Serializable transaction makes
        // the loser fail (serialization 40001, or the unique (SubscriptionId, UserId) index)
        // so we roll back and re-evaluate rather than over-allocating. A unique-index
        // violation means a concurrent insert for the SAME user, which the retry re-reads as
        // already-assigned (idempotent allow). Any residual over-allocation under extreme
        // concurrency is a known CS10 lab limitation; the Serializable isolation is what
        // prevents it in practice.
        for (var attempt = 1; ; attempt++)
        {
            await using var tx = await db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.Serializable, ct);
            try
            {
                var seatsUsed = await db.SeatAssignments
                    .CountAsync(a => a.SubscriptionId == subscription.Id, ct);
                var alreadyAssigned = await db.SeatAssignments
                    .AnyAsync(a => a.SubscriptionId == subscription.Id && a.UserId == request.UserId, ct);

                var decision = SeatDecision.Evaluate(plan.SeatLimit, seatsUsed, alreadyAssigned);

                if (decision.Assigned && !alreadyAssigned)
                {
                    db.SeatAssignments.Add(new SeatAssignment
                    {
                        Id = Guid.NewGuid(),
                        SubscriptionId = subscription.Id,
                        UserId = request.UserId,
                    });
                    await db.SaveChangesAsync(ct);
                }

                await tx.CommitAsync(ct);

                Emit(audit, metrics, tenantCode, EntitlementDecisionType.Seat, "seats",
                    decision.Assigned ? EntitlementOutcome.Allow : EntitlementOutcome.Deny, planTier);

                return TypedResults.Ok(new SeatAssignmentResponse(
                    decision.Assigned, plan.SeatLimit, decision.SeatsUsed, decision.Remaining, decision.Reason));
            }
            catch (Exception ex) when (attempt < MaxSeatAttempts && IsRetryableSeatConflict(ex))
            {
                db.ChangeTracker.Clear();
                await tx.RollbackAsync(ct);
            }
        }
    }

    private static async Task<IResult> ReleaseSeatAsync(
        string tenantCode,
        SeatMutationRequest request,
        EntitlementsDbContext db,
        IEntitlementAuditSink audit,
        EntitlementsMetrics metrics,
        CancellationToken ct)
    {
        var subscription = await db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantCode == tenantCode, ct);
        if (subscription is null)
        {
            Emit(audit, metrics, tenantCode, EntitlementDecisionType.Seat, "seats",
                EntitlementOutcome.Deny, planTier: string.Empty);
            return TypedResults.Ok(new SeatAssignmentResponse(false, 0, 0, 0, NoSubscription));
        }

        var plan = await db.Plans.AsNoTracking().FirstAsync(p => p.Tier == subscription.PlanTier, ct);

        var assignment = await db.SeatAssignments
            .FirstOrDefaultAsync(a => a.SubscriptionId == subscription.Id && a.UserId == request.UserId, ct);
        var found = assignment is not null;
        if (assignment is not null)
        {
            db.SeatAssignments.Remove(assignment);
            await db.SaveChangesAsync(ct);
        }

        var seatsUsed = await db.SeatAssignments.AsNoTracking()
            .CountAsync(a => a.SubscriptionId == subscription.Id, ct);
        var remaining = SeatMath.Remaining(plan.SeatLimit, seatsUsed);

        Emit(audit, metrics, tenantCode, EntitlementDecisionType.Seat, "seats",
            EntitlementOutcome.Allow, subscription.PlanTier.ToString());

        return TypedResults.Ok(new SeatAssignmentResponse(
            false, plan.SeatLimit, seatsUsed, remaining, found ? "seat-released" : "not-assigned"));
    }

    // A seat-assign SaveChanges/commit conflict we can safely retry: EF wraps unique-index
    // and serialization failures as DbUpdateException on SaveChanges, while a Serializable
    // commit-time serialization failure surfaces as a bare PostgresException (40001).
    private static bool IsRetryableSeatConflict(Exception ex) =>
        ex is DbUpdateException
        || ex is PostgresException
        {
            SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation
        };

    private static void Emit(
        IEntitlementAuditSink audit,
        EntitlementsMetrics metrics,
        string tenantCode,
        EntitlementDecisionType type,
        string key,
        EntitlementOutcome outcome,
        string planTier)
    {
        audit.Record(new EntitlementDecision(
            tenantCode, type, key, outcome, planTier, Amount: null, Used: null, Limit: null,
            DateTimeOffset.UtcNow));
        metrics.RecordDecision(type.ToString().ToLowerInvariant(), outcome.ToString().ToLowerInvariant());
    }

    private static void EmitQuota(
        IEntitlementAuditSink audit,
        EntitlementsMetrics metrics,
        string tenantCode,
        string quotaKey,
        EntitlementOutcome outcome,
        string planTier,
        long amount,
        long used,
        long limit,
        long consumed)
    {
        audit.Record(new EntitlementDecision(
            tenantCode, EntitlementDecisionType.Quota, quotaKey, outcome, planTier,
            amount, used, limit, DateTimeOffset.UtcNow));
        metrics.RecordDecision(
            EntitlementDecisionType.Quota.ToString().ToLowerInvariant(),
            outcome.ToString().ToLowerInvariant());
        if (consumed > 0)
        {
            metrics.RecordQuotaConsumed(quotaKey, consumed);
        }
    }
}
