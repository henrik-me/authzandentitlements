using AuthzEntitlements.Authz.Pdp.Contracts;

namespace AuthzEntitlements.Authz.Pdp.Lifecycle;

// Policy-lifecycle DTOs (CS17): the request/result shapes for what-if simulation and
// shadow / dual-run engine comparison. Kept engine-agnostic — they carry the same
// AccessRequest every provider answers, so a candidate engine can be previewed or
// compared against the trusted one on identical input without changing calling code.

// What-if simulation request: evaluate a hypothetical AccessRequest against a chosen engine
// (or the active engine when Engine is blank). A simulation is NOT an enforced decision — it
// answers "what would this policy/engine decide?" so an author can preview a change safely.
public sealed record WhatIfRequest(string? Engine, AccessRequest Request);

// The self-explaining result of a what-if simulation: which engine answered plus the full
// decision (reasons + obligations), so the preview shows exactly what enforcement would do.
public sealed record WhatIfResult(
    string Engine,
    Decision Decision,
    IReadOnlyList<Reason> Reasons,
    IReadOnlyList<Obligation> Obligations);

// One engine's answer flattened to the three fields a comparison keys on: the decision, the
// primary reason code, and the (sorted) obligation ids. Sorting makes obligation comparison
// order-insensitive so two engines that attach the same obligations always compare equal.
public sealed record EngineDecision(
    string Engine,
    Decision Decision,
    string ReasonCode,
    IReadOnlyList<string> ObligationIds);

// A shadow / dual-run request: evaluate the SAME AccessRequest against a Primary engine and one
// or more Shadow engines. Blank Primary uses the active engine; blank/empty Shadows lets the
// endpoint fall back to the deterministic in-process RBAC family. The point is to compare a
// candidate engine to the trusted one on identical input before promoting it.
public sealed record ShadowRunRequest(
    string? Primary,
    IReadOnlyList<string>? Shadows,
    AccessRequest Request);

// One shadow engine compared against the primary for a single request: both flattened
// decisions, whether they agree, and the human-readable divergence lines when they do not.
public sealed record ShadowComparison(
    EngineDecision Primary,
    EngineDecision Shadow,
    bool Agrees,
    IReadOnlyList<string> Divergences);

// The full single-request shadow-run result: the primary decision, a per-shadow comparison,
// and AllAgree — the single boolean a rollout/promotion gate keys on (promote only when every
// shadow matched the trusted primary).
public sealed record ShadowRunResult(
    EngineDecision Primary,
    IReadOnlyList<ShadowComparison> Comparisons,
    bool AllAgree);

// One diverging scenario in a whole-catalog dual run: the scenario id plus both engines'
// flattened decisions and the divergence lines. Only divergences are collected, so an empty
// list means the shadow engine matched the primary across the entire catalog.
public sealed record ScenarioDivergence(
    string ScenarioId,
    EngineDecision Primary,
    EngineDecision Shadow,
    IReadOnlyList<string> Divergences);

// The result of shadowing one engine against another across the whole scenario catalog: the
// two engine names, totals, and the per-scenario divergences. AllAgree is the parity verdict a
// CI gate or migration harness checks before trusting a swap.
public sealed record CatalogShadowReport(
    string Primary,
    string Shadow,
    int Total,
    int Agreements,
    IReadOnlyList<ScenarioDivergence> Divergences)
{
    public bool AllAgree => Divergences.Count == 0;
}

// A whole-catalog shadow request: compare a Shadow engine against a Primary engine (or the
// active engine when Primary is blank) across the full parity catalog.
public sealed record CatalogShadowRequest(string? Primary, string Shadow);
