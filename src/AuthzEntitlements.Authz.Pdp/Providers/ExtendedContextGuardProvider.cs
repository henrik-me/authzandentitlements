using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.ServiceDefaults;
using Microsoft.Extensions.Logging;

namespace AuthzEntitlements.Authz.Pdp.Providers;

// CS45 fail-closed guard: a decorator that wraps a provider which does NOT declare
// ISupportsExtendedAuthorizationContext, so any request carrying the CS19/CS21 extended-authorization
// context — on-behalf-of (Subject.Actor), manager->delegate delegation (Context.Delegation), or
// break-glass elevation (Context.BreakGlass) — is DENIED rather than forwarded to an engine that
// would evaluate it by the human subject alone (a silent fail-OPEN on an engine swap). Every other
// request is passed through to the inner provider byte-for-byte, so the guard is transparent to the
// base (non-delegated) decision, including any engine-native Explanation the inner provider attaches.
//
// AuthorizationDecisionProviderFactory applies this decorator centrally (Decision #2), so all four
// factory-resolved paths — the enforced PdpDecisionService, plus the ShadowRunner / WhatIfEvaluator /
// PlaygroundFanoutService what-if surfaces — are guarded uniformly, and a capable engine (currently
// only the reference) is never wrapped. The guard NEVER permits and NEVER throws on the trigger: it
// fails closed to a Deny with the distinct ReasonCodes.ExtendedContextUnsupported reason.
//
// It deliberately does NOT implement ISupportsExtendedAuthorizationContext: the guard does not honour
// the extended context, it refuses it. If it declared the marker the factory would leave engines
// unguarded — the exact fail-open this class prevents.
public sealed class ExtendedContextGuardProvider : IAuthorizationDecisionProvider
{
    // Stable, non-sensitive caller-facing message. It embeds no request-derived value (subject/actor
    // ids, scopes, grant ids), so the anonymous /api/authz/evaluate surface cannot leak request detail
    // through the deny reason; the specific triggering field + engine are LOGGED instead (sanitized).
    private const string DenyMessage =
        "The selected authorization engine does not natively support on-behalf-of / delegation / " +
        "break-glass (extended-authorization) requests; failing closed.";

    private readonly IAuthorizationDecisionProvider _inner;
    private readonly ILogger? _logger;

    public ExtendedContextGuardProvider(IAuthorizationDecisionProvider inner, ILogger? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _logger = logger;
    }

    // The wrapped provider, exposed read-only for diagnostics/telemetry that need the concrete engine
    // behind the guard (the guard itself re-exposes the inner Name, so selection/parity are unchanged).
    public IAuthorizationDecisionProvider Inner => _inner;

    // Re-expose the inner engine's name so name-based selection, parity comparison, and telemetry keep
    // seeing the real engine (e.g. "cerbos"), not the guard — the wrapping is invisible to resolution.
    public string Name => _inner.Name;

    public AccessDecision Evaluate(AccessRequest request)
    {
        // Identify which extended-context field triggered the guard (if any). The label is a constant,
        // NOT a request-derived value, so it is safe to log and to keep the deny reason stable.
        var trigger = ExtendedContextTrigger(request);
        if (trigger is null)
        {
            // No extended-authorization context: the guard is transparent — forward unchanged so the
            // inner decision (and its Explanation/Obligations) passes through byte-for-byte.
            return _inner.Evaluate(request);
        }

        // One sanitized (CWE-117 / LRN-059) warning naming the engine + the triggering field. The
        // engine name is sanitized defensively (consistent with PdpDecisionService); the trigger label
        // is a constant. No request identity value is logged here.
        _logger?.LogWarning(
            "Extended-authorization context is unsupported by provider '{Provider}' (triggered by " +
            "{Trigger}); failing closed with {Reason}.",
            LogSanitizer.Clean(_inner.Name),
            trigger,
            ReasonCodes.ExtendedContextUnsupported);

        return AccessDecision.Deny(new Reason(ReasonCodes.ExtendedContextUnsupported, DenyMessage));
    }

    // Names the first present extended-context field, or null when the request carries none. The order
    // is purely for a legible log label; presence of ANY of the three triggers the fail-closed deny.
    private static string? ExtendedContextTrigger(AccessRequest request)
    {
        if (request.Subject.Actor is not null)
        {
            return "Subject.Actor";
        }

        if (request.Context.Delegation is not null)
        {
            return "Context.Delegation";
        }

        if (request.Context.BreakGlass is not null)
        {
            return "Context.BreakGlass";
        }

        return null;
    }
}
