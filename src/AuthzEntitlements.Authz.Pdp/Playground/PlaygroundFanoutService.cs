using System.Diagnostics;
using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using Microsoft.Extensions.Logging;

namespace AuthzEntitlements.Authz.Pdp.Playground;

// The AuthZ Playground fan-out service (CS15): evaluate ONE AccessRequest against every registered
// engine (or a named subset) and return per-engine comparable results. This is a NON-AUDITED,
// what-if surface — exactly like the CS17 WhatIfEvaluator / /shadow tooling — so it resolves
// providers DIRECTLY through the factory and NEVER goes through PdpDecisionService (which would emit
// a real enforcement audit event + decision metric). Authors use it to see how the engines compare
// on the same input; nothing here is an enforced decision.
public sealed class PlaygroundFanoutService
{
    // The fail-closed reason code the OPA/OpenFGA/Cedar adapters (and this service, on a throw) use
    // when their engine cannot be reached. Substring match is case-insensitive so both
    // "ProviderUnavailable" and "EngineUnavailable" classify as unavailable.
    private const string UnavailableMarker = "unavailable";

    // Sanitized, caller-facing message used when a provider throws. The real exception (which may
    // carry internal/sensitive detail) is logged server-side, never returned to the anonymous
    // fan-out caller.
    private const string EngineThrewMessage = "The engine threw an unexpected error during evaluation.";

    private readonly AuthorizationDecisionProviderFactory _factory;
    private readonly ILogger<PlaygroundFanoutService> _logger;

    public PlaygroundFanoutService(
        AuthorizationDecisionProviderFactory factory, ILogger<PlaygroundFanoutService> logger)
    {
        _factory = factory;
        _logger = logger;
    }

    // Fan out one request across the given engines (or every registered provider when none are named)
    // and return the per-engine results plus the cross-engine agreement verdict. Each engine is asked
    // exactly once; a single engine throwing never aborts the whole fan-out (defensive — providers are
    // meant to fail closed, but a fan-out surface must survive one misbehaving engine).
    public PlaygroundFanoutResponse Fanout(AccessRequest request, IReadOnlyList<string>? engines)
    {
        var names = ResolveEngineNames(engines);

        var results = new List<EngineDecisionResult>(names.Count);
        foreach (var name in names)
        {
            results.Add(Evaluate(name, request));
        }

        // AllAgree considers only AVAILABLE engines: an unreachable engine's fail-closed Deny must not
        // be mistaken for a genuine disagreement. 0 or 1 available engines trivially agree.
        var available = results.Where(r => r.Available).ToList();
        var allAgree = available.Count <= 1
            || available.All(r => r.Decision == available[0].Decision);

        return new PlaygroundFanoutResponse(results, CurrentTraceId(), allAgree);
    }

    // The engine list to fan out over: the provided names (trimmed, blanks dropped, de-duped
    // case-insensitively) or every registered provider when none are usably named. An all-blank or
    // empty list falls back to every provider (matching the "when none are named" contract) rather
    // than fanning out over zero engines (whose AllAgree would be trivially true).
    private IReadOnlyList<string> ResolveEngineNames(IReadOnlyList<string>? engines)
    {
        var named = engines?
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return named is { Count: > 0 } ? named : _factory.ProviderNames;
    }

    // Evaluate a single engine, measuring the one Evaluate call and classifying availability. The
    // call is wrapped in try/catch so a throwing engine is reported as unavailable (a synthesized
    // fail-closed Deny) rather than aborting the fan-out.
    private EngineDecisionResult Evaluate(string engine, AccessRequest request)
    {
        var provider = _factory.GetProvider(engine);

        var startTimestamp = Stopwatch.GetTimestamp();
        AccessDecision decision;
        try
        {
            decision = provider.Evaluate(request);
        }
        catch (Exception ex)
        {
            // Log the real exception server-side; return only a sanitized message to the caller
            // (the fan-out endpoint is anonymous, so the message must not leak internal detail).
            _logger.LogWarning(ex, "Playground fan-out: engine {Engine} threw during evaluation.", provider.Name);
            var latencyOnThrow = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            var denied = AccessDecision.Deny(new Reason("ProviderUnavailable", EngineThrewMessage));
            return Result(provider.Name, denied, latencyOnThrow, available: false, unavailableReason: EngineThrewMessage);
        }

        var latencyMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

        // Availability heuristic: an engine is unavailable when it Denies with a reason whose Code
        // contains "unavailable" (the ProviderUnavailable/EngineUnavailable fail-closed codes the
        // OPA/OpenFGA/Cedar adapters emit when their engine can't be reached). Its message becomes the
        // UnavailableReason so the UI can explain the gap.
        var unavailableReason = decision.Decision == Decision.Deny
            ? decision.Reasons.FirstOrDefault(
                r => r.Code.Contains(UnavailableMarker, StringComparison.OrdinalIgnoreCase))?.Message
            : null;

        return Result(provider.Name, decision, latencyMs, available: unavailableReason is null, unavailableReason);
    }

    // Build a result row, guaranteeing an explanation is present (mirrors PdpDecisionService: use the
    // provider's engine-native explanation when it attached one, otherwise the shared baseline derived
    // from the primary reason) so the playground always has a "why" to render for every engine.
    private static EngineDecisionResult Result(
        string engine, AccessDecision decision, double latencyMs, bool available, string? unavailableReason)
    {
        var explanation = decision.Explanation ?? DecisionExplanations.Baseline(engine, decision);
        return new EngineDecisionResult(
            Engine: engine,
            Decision: decision.Decision,
            Reasons: decision.Reasons,
            Obligations: decision.Obligations,
            Explanation: explanation,
            LatencyMs: latencyMs,
            TraceId: CurrentTraceId(),
            Available: available,
            UnavailableReason: unavailableReason);
    }

    // The ambient trace id, best-effort: null when no listener is sampling (e.g. tests, or an
    // un-instrumented run). We deliberately do NOT start our own ActivitySource span — the playground
    // is a passive what-if surface; it only surfaces whatever trace context is already flowing.
    private static string? CurrentTraceId() =>
        Activity.Current?.TraceId.ToString();
}
