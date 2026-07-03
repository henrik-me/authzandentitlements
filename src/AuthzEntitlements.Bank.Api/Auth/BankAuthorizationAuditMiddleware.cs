using System.Diagnostics;
using System.Security.Claims;

namespace AuthzEntitlements.Bank.Api.Auth;

// Wraps every /api request and emits one structured, audit-ready event describing
// the fine-grained authorization decision, plus activity tags for telemetry. It
// runs BEFORE authentication so it observes the FINAL status the pipeline produced,
// including the 401/403 that auth/authz (or an endpoint-level Forbid for
// tenant-mismatch/maker-checker/SoD) short-circuit.
//
// Unlike the edge gateway proxy, Bank.Api is the terminal fine decider: its own
// 401/403 ARE its authorization decisions, so classification needs no
// proxy-pipeline marker logic — the status code alone is authoritative.
public sealed class BankAuthorizationAuditMiddleware(
    RequestDelegate next,
    ILogger<BankAuthorizationAuditMiddleware> logger)
{
    private const string ApiPathPrefix = "/api";

    // Fine decision outcomes.
    public const string DecisionAllow = "allow";
    public const string DecisionDeny = "deny";

    // Why the gate allowed/denied — the audit-ready reason vocabulary, kept
    // consistent across the structured audit event and the activity tags.
    public const string ReasonAuthorized = "authorized";
    public const string ReasonUnauthenticated = "unauthenticated";
    public const string ReasonForbidden = "forbidden";

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip infrastructure paths (root info + health/liveness). Only the /api
        // surface is subject to fine-grained authorization and worth auditing.
        if (!context.Request.Path.StartsWithSegments(ApiPathPrefix))
        {
            await next(context);
            return;
        }

        await next(context);

        var statusCode = context.Response.StatusCode;
        var (decision, reason) = ClassifyDecision(statusCode);

        var user = context.User;
        var tenant = user.GetTenant();
        var subject = user.GetSubjectId()?.ToString();
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        var auditEvent = new BankAuditEvent(
            TimestampUtc: DateTimeOffset.UtcNow,
            TraceId: traceId,
            Method: context.Request.Method,
            Path: context.Request.Path.Value ?? string.Empty,
            Decision: decision,
            Reason: reason,
            Subject: subject,
            Tenant: tenant,
            StatusCode: statusCode);

        // Structured template names every field so CS13's Audit.Service can ingest
        // the event verbatim without parsing a free-text message.
        logger.LogInformation(
            "Bank fine decision {Decision} ({Reason}) for {Method} {Path} " +
            "status={StatusCode} subject={Subject} tenant={Tenant} " +
            "trace={TraceId} at {TimestampUtc}",
            auditEvent.Decision,
            auditEvent.Reason,
            auditEvent.Method,
            auditEvent.Path,
            auditEvent.StatusCode,
            auditEvent.Subject,
            auditEvent.Tenant,
            auditEvent.TraceId,
            auditEvent.TimestampUtc);

        var activity = Activity.Current;
        activity?.SetTag("authz.decision", decision);
        activity?.SetTag("authz.reason", reason);
        activity?.SetTag("authz.tenant", tenant);
    }

    // Pure, unit-testable classification of the terminal status into the fine
    // decision/reason vocabulary. Bank.Api's own 401/403 are its authorization
    // decisions; any other status (2xx or a business 400/404/409) means the authz
    // gate permitted the request and downstream is domain logic — an allow.
    public static (string Decision, string Reason) ClassifyDecision(int statusCode) => statusCode switch
    {
        StatusCodes.Status401Unauthorized => (DecisionDeny, ReasonUnauthenticated),
        StatusCodes.Status403Forbidden => (DecisionDeny, ReasonForbidden),
        _ => (DecisionAllow, ReasonAuthorized),
    };
}
