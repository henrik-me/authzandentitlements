using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Playground;

// The request/response DTOs for the AuthZ Playground fan-out (CS15): run ONE AccessRequest across
// ALL registered engines and return per-engine comparable results. This is a what-if / non-audited
// surface (mirroring the CS17 WhatIfEvaluator / /shadow), so the wire shape deliberately reuses the
// existing Contracts types (Decision, Reason, Obligation, DecisionExplanation) — the per-engine
// result serializes identically to /evaluate, which the Bank.Web playground client mirrors.

// One fan-out request: the AccessRequest to evaluate, plus an optional engine subset. A null or
// empty Engines list fans out across every registered provider (factory.ProviderNames).
public sealed record PlaygroundFanoutRequest(
    AccessRequest Request,
    IReadOnlyList<string>? Engines = null);

// One engine's answer to the fanned-out request. Decision/Reasons/Obligations/Explanation reuse the
// Contracts types so a result serializes exactly like /evaluate. LatencyMs is the measured cost of
// the single Evaluate call; TraceId is the best-effort ambient trace id (may be null when nothing is
// sampling). Available is the heuristic reachability verdict (see PlaygroundFanoutService); when
// false, UnavailableReason carries the exception message or the fail-closed reason's message.
public sealed record EngineDecisionResult(
    string Engine,
    Decision Decision,
    IReadOnlyList<Reason> Reasons,
    IReadOnlyList<Obligation> Obligations,
    DecisionExplanation? Explanation,
    double LatencyMs,
    string? TraceId,
    bool Available,
    string? UnavailableReason);

// The whole fan-out: every engine's result, the top-level best-effort trace id, and AllAgree — true
// when all AVAILABLE engines returned the same Decision (true when 0 or 1 engines are available).
public sealed record PlaygroundFanoutResponse(
    IReadOnlyList<EngineDecisionResult> Results,
    string? TraceId,
    bool AllAgree);
