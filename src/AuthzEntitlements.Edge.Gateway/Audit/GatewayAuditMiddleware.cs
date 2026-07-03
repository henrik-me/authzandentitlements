using System.Diagnostics;
using AuthzEntitlements.Edge.Gateway.Auth;
using AuthzEntitlements.Edge.Gateway.Telemetry;
using Yarp.ReverseProxy.Model;

namespace AuthzEntitlements.Edge.Gateway.Audit;

// Wraps every proxied /api request and emits one structured, audit-ready event
// plus telemetry describing the coarse decision. It runs BEFORE authentication
// so it observes the FINAL status the pipeline produced, including the 401/403
// that auth/authz short-circuit — those never reach the reverse proxy but must
// still be audited at the edge.
public sealed class GatewayAuditMiddleware(
    RequestDelegate next,
    ILogger<GatewayAuditMiddleware> logger,
    GatewayMetrics metrics)
{
    private const string ApiPathPrefix = "/api";

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip infrastructure paths (root info + health/liveness). Only calls that
        // are subject to coarse authorization (the proxied /api surface) are audited.
        if (!context.Request.Path.StartsWithSegments(ApiPathPrefix))
        {
            await next(context);
            return;
        }

        await next(context);

        var user = context.User;
        var tenant = user.GetTenant();
        var (decision, reason) = ClassifyDecision(context.Response.StatusCode, tenant is not null);

        var routeConfig = context.Features.Get<IReverseProxyFeature>()?.Route.Config;
        var routeId = routeConfig?.RouteId;
        var requiredScope = MapPolicyToScope(routeConfig?.AuthorizationPolicy);

        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        var auditEvent = new GatewayAuditEvent(
            TimestampUtc: DateTimeOffset.UtcNow,
            TraceId: traceId,
            Method: context.Request.Method,
            Path: context.Request.Path.Value ?? string.Empty,
            RouteId: routeId,
            Decision: decision,
            Reason: reason,
            Subject: user.GetSubject(),
            Tenant: tenant,
            RequiredScope: requiredScope,
            Audience: GatewayAuthenticationSetup.DefaultAudience);

        // Structured template names every field so CS13's Audit.Service can ingest
        // the event verbatim without parsing a free-text message.
        logger.LogInformation(
            "Gateway coarse decision {Decision} ({Reason}) for {Method} {Path} " +
            "route={RouteId} status={StatusCode} subject={Subject} tenant={Tenant} " +
            "scope={RequiredScope} audience={Audience} trace={TraceId} at {TimestampUtc}",
            auditEvent.Decision,
            auditEvent.Reason,
            auditEvent.Method,
            auditEvent.Path,
            auditEvent.RouteId,
            context.Response.StatusCode,
            auditEvent.Subject,
            auditEvent.Tenant,
            auditEvent.RequiredScope,
            auditEvent.Audience,
            auditEvent.TraceId,
            auditEvent.TimestampUtc);

        metrics.RecordDecision(decision, reason, routeId);

        var activity = Activity.Current;
        activity?.SetTag("gateway.decision", decision);
        activity?.SetTag("gateway.reason", reason);
        activity?.SetTag("gateway.tenant", tenant);
        activity?.SetTag("gateway.route", routeId);
    }

    // Pure, unit-testable classification of a final status into the coarse
    // decision/reason vocabulary. 401 is an unauthenticated challenge; 403 is a
    // forbid that — given the token reached authorization — distinguishes a
    // missing tenant claim from a missing scope. Anything below 400 (and any
    // downstream 5xx the proxy forwarded) counts as allowed/routed at the edge.
    public static (string Decision, string Reason) ClassifyDecision(int statusCode, bool hasTenant)
        => statusCode switch
        {
            StatusCodes.Status401Unauthorized =>
                (GatewayTelemetry.DecisionDeny, GatewayTelemetry.ReasonUnauthenticated),
            StatusCodes.Status403Forbidden =>
                (GatewayTelemetry.DecisionDeny,
                    hasTenant ? GatewayTelemetry.ReasonMissingScope : GatewayTelemetry.ReasonMissingTenant),
            _ => (GatewayTelemetry.DecisionAllow, GatewayTelemetry.ReasonRouted),
        };

    // Best-effort map from a matched route's coarse policy to the scope it
    // requires, for the audit record. AuthenticatedPolicy (and any unmatched
    // route) has no required scope, so null is the correct value.
    private static string? MapPolicyToScope(string? policy) => policy switch
    {
        CoarseAuthorization.ReadPolicy => CoarseAuthorization.ReadScope,
        CoarseAuthorization.TransactionsWritePolicy => CoarseAuthorization.TransactionsWriteScope,
        CoarseAuthorization.ApprovalsWritePolicy => CoarseAuthorization.ApprovalsWriteScope,
        _ => null,
    };
}
