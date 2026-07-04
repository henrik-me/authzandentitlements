using System.Diagnostics;
using AuthzEntitlements.Edge.Gateway.Auth;
using AuthzEntitlements.Edge.Gateway.Telemetry;
using AuthzEntitlements.ServiceDefaults;
using Yarp.ReverseProxy.Configuration;
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
    GatewayMetrics metrics,
    IConfiguration configuration)
{
    private const string ApiPathPrefix = "/api";

    // The configured expected audience, resolved once. Reported on every audit event
    // so it reflects the actual Keycloak:Audience configuration, not just the default.
    private readonly string _audience = GatewayAuthenticationSetup.ResolveAudience(configuration);

    // Set by a marker middleware that runs only after the coarse authorization
    // policy has passed (UseAuthorization short-circuits denied requests before
    // it). Its presence distinguishes an EDGE deny (absent) from an edge allow
    // that was routed to Bank.Api (present), so a downstream fine-grained 403 is
    // audited as allow/routed rather than as a coarse edge deny.
    public const string EdgeAuthorizedItemKey = "gateway.edge.authorized";

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

        var edgeAuthorized = context.Items.ContainsKey(EdgeAuthorizedItemKey);
        var statusCode = context.Response.StatusCode;

        // An /api request that neither cleared the edge coarse policy (edgeAuthorized)
        // nor was short-circuited by it (401/403) matched no coarse route — e.g. an
        // unmatched method that 404s. That is not a coarse decision, so skip it.
        if (!ShouldAudit(edgeAuthorized, statusCode))
        {
            return;
        }

        var user = context.User;
        var tenant = user.GetTenant();
        var (decision, reason) = ClassifyDecision(statusCode, tenant is not null, edgeAuthorized);

        // Resolve the matched route's id + required scope. On an edge allow/routed the
        // IReverseProxyFeature is set (the proxy pipeline ran); on a short-circuit 401/403
        // deny it is UNSET (UseAuthorization short-circuited before the proxy), so fall
        // back to the matched endpoint's YARP route metadata — routing has already run,
        // so context.GetEndpoint() still carries the RouteModel even on a deny (LRN-013).
        var (routeId, requiredScope) = ResolveRouteMetadata(
            context.Features.Get<IReverseProxyFeature>()?.Route.Config,
            context.GetEndpoint());

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
            Audience: _audience);

        // Structured template names every field so CS13's Audit.Service can ingest
        // the event verbatim without parsing a free-text message.
        logger.LogInformation(
            "Gateway coarse decision {Decision} ({Reason}) for {Method} {Path} " +
            "route={RouteId} status={StatusCode} subject={Subject} tenant={Tenant} " +
            "scope={RequiredScope} audience={Audience} trace={TraceId} at {TimestampUtc}",
            LogSanitizer.Clean(auditEvent.Decision),
            LogSanitizer.Clean(auditEvent.Reason),
            LogSanitizer.Clean(auditEvent.Method),
            LogSanitizer.Clean(auditEvent.Path),
            LogSanitizer.Clean(auditEvent.RouteId),
            context.Response.StatusCode,
            LogSanitizer.Clean(auditEvent.Subject),
            LogSanitizer.Clean(auditEvent.Tenant),
            LogSanitizer.Clean(auditEvent.RequiredScope),
            LogSanitizer.Clean(auditEvent.Audience),
            LogSanitizer.Clean(auditEvent.TraceId),
            auditEvent.TimestampUtc);

        metrics.RecordDecision(decision, reason, routeId);

        var activity = Activity.Current;
        activity?.SetTag("gateway.decision", decision);
        activity?.SetTag("gateway.reason", reason);
        activity?.SetTag("gateway.tenant", tenant);
        activity?.SetTag("gateway.route", routeId);
    }

    // Whether an /api request represents a coarse authorization decision worth
    // auditing. A routed request (edgeAuthorized) always is; otherwise only an edge
    // short-circuit (401/403) is — any other status means no coarse route matched
    // (e.g. an unmatched method that 404s), which is not a coarse decision.
    public static bool ShouldAudit(bool edgeAuthorized, int statusCode)
        => edgeAuthorized
            || statusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden;

    // Resolves the matched route's id + required scope for the audit event from the two
    // sources that can carry it, in priority order:
    //   1. the YARP IReverseProxyFeature route config — present on an edge allow/routed
    //      (the proxy pipeline ran and forwarded the request), OR
    //   2. the matched endpoint's YARP RouteModel metadata — the fallback for a
    //      short-circuit 401/403 deny, where UseAuthorization short-circuited before the
    //      proxy so the feature is unset, but routing already matched the route so the
    //      endpoint (and its RouteModel) is still on the context (LRN-013).
    // An unmatched 404 (null endpoint) or a method-mismatch 405 (ASP.NET's synthetic
    // endpoint carries no RouteModel) yields (null, null) — but those are skipped by
    // ShouldAudit before this runs, so a non-decision never reaches an audit event.
    public static (string? RouteId, string? RequiredScope) ResolveRouteMetadata(
        RouteConfig? proxyRouteConfig, Endpoint? endpoint)
    {
        var routeConfig = proxyRouteConfig
            ?? endpoint?.Metadata.GetMetadata<RouteModel>()?.Config;
        return (routeConfig?.RouteId, MapPolicyToScope(routeConfig?.AuthorizationPolicy));
    }

    // Pure, unit-testable classification of a request into the coarse
    // decision/reason vocabulary. The primary discriminator is whether the request
    // cleared the edge coarse policy (edgeAuthorized): once it clears the edge and
    // is routed, ANY resulting status — including a fine-grained 401/403 from
    // Bank.Api or a downstream 5xx — is an edge allow/routed, NOT a coarse deny.
    // Only a request short-circuited AT the edge is a deny, and there the status
    // distinguishes an unauthenticated challenge (401) from a forbid (403) whose
    // reason is a missing tenant claim vs a missing scope.
    public static (string Decision, string Reason) ClassifyDecision(
        int statusCode, bool hasTenant, bool edgeAuthorized)
    {
        if (edgeAuthorized)
        {
            return (GatewayTelemetry.DecisionAllow, GatewayTelemetry.ReasonRouted);
        }

        return statusCode == StatusCodes.Status401Unauthorized
            ? (GatewayTelemetry.DecisionDeny, GatewayTelemetry.ReasonUnauthenticated)
            : (GatewayTelemetry.DecisionDeny,
                hasTenant ? GatewayTelemetry.ReasonMissingScope : GatewayTelemetry.ReasonMissingTenant);
    }

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
