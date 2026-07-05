# CS45 — Delegation/OBO fail-closed guard across swappable PDP adapters

**Status:** active
**Owner:** yoga-ae-c4
**Branch:** cs45/content
**Started:** 2026-07-05
**Closed:** —
**Filed by:** yoga-ae-c4 on 2026-07-04 — surfaced by the CS26 Cerbos review (PR #139), which caught that the Cerbos adapter ignored `Subject.Actor`/`Context.Delegation` and could permit an on-behalf-of call the reference denies; investigation showed the gap is cross-cutting (only `ReferenceDecisionProvider` handles OBO; `FintechRuleEvaluator` has zero delegation handling).
**Phase:** Cross-cutting
**Lane:** Cross-cutting
**Depends on:** CS19, CS21, CS26

## Goal

Close the cross-cutting **fail-open** where a non-reference PDP engine, selected via `Pdp:Provider`, evaluates an on-behalf-of / delegation / break-glass request by looking only at the human subject — permitting access the CS19/CS21 reference logic would **deny** (e.g. the delegate/actor lacks the required delegated scope, or the grant is expired). Guarantee that swapping engines can never silently weaken the OBO/delegation/break-glass constraints.

## Background

- **CS19/CS21** added on-behalf-of + delegation + break-glass to the decision contract: `Subject.Actor` (the OBO delegate), `EvaluationContext.Delegation` (a manager→delegate grant), and `EvaluationContext.BreakGlass` (an emergency elevation). `ReferenceDecisionProvider` constrains the decision to the intersection of the human's rights and the actor's delegated scopes, and honours grant expiry.
- **Only the reference provider handles these.** `FintechRuleEvaluator` (shared by the `aspnet` + `casbin` adapters) has **zero** `Actor`/`Delegation`/`BreakGlass` handling (verified 2026-07-04); the out-of-process adapters (`opa`, `openfga`, `spicedb`) map only the human subject; `cedar`'s in-process policy set likewise. So every non-reference engine can diverge from the reference on an OBO/delegation/break-glass request.
- **CS26 Cerbos review (PR #139)** flagged this as a fail-OPEN blocker for the `cerbos` full-decision adapter and it was fixed **in-adapter** with a fail-closed short-circuit on `Subject.Actor`/`Context.Delegation`/`Context.BreakGlass` (documented boundary in `docs/authz/cerbos-adapter.md`). The already-merged `openfga`/`spicedb`/`opa`/`casbin`/`aspnet`/`cedar` adapters have the SAME gap and no guard.
- **Severity is provider-dependent:** the default engine is `reference` (which handles OBO), so the fail-open only manifests when an operator selects a non-reference provider. But the entire point of the CS05 swap seam is decision-equivalence across engines, so a silent OBO weakening on swap is a genuine security regression the swap contract must not allow.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Fail-closed vs. per-engine delegation | A shared **fail-closed guard** for engines that do not natively support OBO; per-engine delegation encoding is explicitly OUT of scope | Uniform fail-closed is the safe default and small; teaching each engine's native policy language delegation is a large, per-engine effort better done as future expansion. Fail-closed can never fail open. |
| 2 | Where the guard lives | In/under **`AuthorizationDecisionProviderFactory`** — a fail-closed decorator the factory wraps around any selected provider that lacks the extended-context capability — NOT in `PdpDecisionService` alone, and NOT copied into each adapter | `PdpDecisionService` wraps only the enforced provider, but `PlaygroundFanoutService`, `ShadowRunner`, and `WhatIfEvaluator` resolve providers **directly** through the factory; guarding at the factory covers all four paths and preserves the service's audit/telemetry wrapping. A per-adapter guard (as CS26 did for Cerbos) must be remembered for every current and future engine. |
| 3 | How a provider opts out | A capability marker **`ISupportsExtendedAuthorizationContext`** (covering OBO/delegation AND break-glass — see Decision #4) that the reference (and any future context-aware engine) declares; the factory guard fails closed for providers that do NOT declare it | Capability-based is future-proof and explicit; avoids a `Name == "reference"` check that rots when a new context-aware engine is added. Naming it for the extended-context fields (not just delegation) matches the trigger set + lets break-glass be supported independently later. |
| 4 | Trigger condition | Fail closed when `Subject.Actor != null` OR `Context.Delegation != null` OR `Context.BreakGlass != null` and the selected provider lacks the extended-context capability | These are exactly the three CS19/CS21 delegation-bearing fields; presence of any means the request needs extended-context semantics the engine can't provide. |
| 5 | Fail-closed reason | Deny with a **distinct** stable reason **`ExtendedContextUnsupported`** (do NOT reuse `ProviderUnavailable`) + a sanitized log; never a permit, never a throw | `PlaygroundFanoutService` classifies deny reasons containing "unavailable" as an engine outage and excludes them from `allAgree`; a deliberate semantic boundary must not be misclassified as an outage. Matches the fail-closed doctrine + the CS26 precedent. |
| 6 | Reconcile the CS26 Cerbos in-adapter guard | Remove Cerbos's per-adapter guard once the shared seam guard lands (or keep it as documented defense-in-depth) — decide in implementation, but there must be ONE authoritative guard | Avoids two divergent guards; the shared seam is the source of truth. |

## Deliverables

- A fail-closed guard **at `AuthorizationDecisionProviderFactory`** (a decorator the factory wraps around any selected provider lacking the extended-context capability) that denies any request carrying `Subject.Actor` / `Context.Delegation` / `Context.BreakGlass` with the distinct reason `ExtendedContextUnsupported` — covering the enforced path **and** the factory-resolved playground/shadow/what-if paths, and leaving the `reference` provider (and any future capable engine) unaffected.
- A capability marker `ISupportsExtendedAuthorizationContext` by which a provider declares native OBO/delegation/break-glass support; `ReferenceDecisionProvider` declares it.
- Reconciliation of the CS26 Cerbos in-adapter guard with the shared guard (single authoritative guard).
- Shared regression tests: **every** non-capable registered provider fails closed (reason `ExtendedContextUnsupported`) on OBO/delegation/break-glass requests via BOTH the enforced seam and the factory-resolved paths; the reference still applies its CS19/CS21 OBO logic (permit/deny) unchanged; the non-delegated scenario catalog is unaffected.
- Contract docs updated (`docs/authz/pdp-contract.md` + `docs/authz/adding-an-engine-adapter.md`) to state the OBO boundary + how an engine opts in; per-adapter boundary notes reconciled.
- A `LEARNINGS.md` entry capturing the cross-cutting fail-open + the factory-guard remedy.

## User-approval gates

- None expected; this is a security-hardening change. If the chosen seam materially changes `PdpDecisionService`'s public shape or the `IAuthorizationDecisionProvider` contract, surface it in review.

## Exit criteria

- No non-reference provider can fail open on an OBO/delegation/break-glass request; a shared test enumerating all registered providers proves each fails closed while the reference is unaffected. The adapter contract documents the boundary and the opt-in. `dotnet build` 0/0 and the full test suite green.

## Risks + open questions

- **Placement.** The guard must sit at the **factory** (so it covers the enforced path AND the direct factory-resolved playground / shadow / what-if paths) and must NOT intercept the reference — confirm the factory decorator composes cleanly with the audit/telemetry wrapping in `PdpDecisionService`.
- **Capability vs. name check.** Prefer the `ISupportsExtendedAuthorizationContext` capability marker over a `Name == "reference"` check (Decision #3); confirm the factory/DI can thread the capability cleanly.
- **Cedar.** `cedar` is in-process and *could* encode delegation natively later; the guard should still fail it closed until it declares support (don't special-case).
- **Double-guard.** Decide whether Cerbos keeps its in-adapter guard as defense-in-depth or delegates entirely to the seam (Decision #6).

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | cs45-47-plan-review (rubber-duck) | e66da0da0945 | 2026-07-05T02:48:12Z | Go-with-amendments | Factory-level guard (enforced + playground/shadow/what-if, not PdpDecisionService-only); distinct ExtendedContextUnsupported reason; capability includes break-glass — all applied. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| Capability marker `ISupportsExtendedAuthorizationContext` (Contracts) + `ReferenceDecisionProvider` declares it | done | yoga-ae-c4 | Decision #3 — empty marker; reference is the only engine that declares it |
| `ExtendedContextUnsupported` reason code, distinct from `ProviderUnavailable` | done | yoga-ae-c4 | Decision #5 — free of the "unavailable" substring so `PlaygroundFanoutService.allAgree` treats it as a genuine deny, not an outage (tested) |
| Fail-closed guard decorator; factory wraps it around any non-capable provider (`GetActiveProvider`/`GetProvider`/`TryGetProvider`) | done | yoga-ae-c4 | Decisions #2/#4 — `ExtendedContextGuardProvider`; covers enforced + playground/shadow/what-if; denies on `Actor`/`Delegation`/`BreakGlass`, never permits/throws, transparent otherwise |
| Reconcile the CS26 Cerbos in-adapter guard (single authoritative guard) | done | yoga-ae-c4 | Decision #6 — removed the per-adapter guard; the factory seam is the single authoritative guard (Cerbos still non-capable → factory-guarded) |
| Tests: every non-capable provider fails closed (`ExtendedContextUnsupported`) via BOTH enforced + factory-resolved paths; reference OBO permit/deny unaffected; non-delegated catalog unaffected | done | yoga-ae-c4 | `ExtendedContextGuardTests` (parameterized over the real graph); PDP 922/922, full solution 1677/1677 |
| Docs: `docs/authz/pdp-contract.md` + `docs/authz/adding-an-engine-adapter.md` — OBO/delegation/break-glass boundary + how an engine opts in | done | yoga-ae-c4 | + `docs/authz/cerbos-adapter.md` boundary → factory-guard pointer |
| Close-out: docs + restart state | done | yoga-ae-c4 | CONTEXT.md updated; WORKBOARD row removed at close-out; contract docs shipped in #159 |
| Close-out: learnings + follow-ups | done | yoga-ae-c4 | LRN-075 flipped → applied (CS45 is the durable factory-guard remedy); no new follow-ups |

## Notes / Learnings

- **Shipped (content PR #159).** The cross-cutting OBO/delegation/break-glass fail-OPEN is closed by a
  capability-gated, fail-closed decorator applied centrally at the provider factory — not per-adapter.
  `ISupportsExtendedAuthorizationContext` marks an engine that natively honours the CS19/CS21 extended
  context; `ExtendedContextGuardProvider` wraps every engine that does NOT, denying any request carrying
  `Subject.Actor`/`Context.Delegation`/`Context.BreakGlass` with the distinct `ExtendedContextUnsupported`
  reason (never permits, never throws, transparent otherwise). `AuthorizationDecisionProviderFactory`
  applies it so the enforced `PdpDecisionService` AND the factory-resolved shadow/what-if/playground paths
  are all covered; only `reference` declares the capability and is unwrapped.
- **Cerbos guard reconciled (Decision #6).** Removed the CS26 Cerbos in-adapter OBO short-circuit — the
  factory seam is now the single authoritative guard (Cerbos remains non-capable, so it is factory-guarded).
- **Reason-code semantics (Decision #5).** `ExtendedContextUnsupported` deliberately omits the substring
  "unavailable" so `PlaygroundFanoutService` treats the boundary as a genuine disagreement, not an engine
  outage (directly tested).
- **LRN-075 flipped → applied** — CS45 is the durable factory-guard remedy for the cross-cutting fail-open.
- **Verification.** `dotnet build` 0/0; PDP suite 922/922; full solution 1677/1677; `harness lint` clean.
  Independent GPT-5.5 security review (adversarial fail-open hunt) → Go, no blocking/non-blocking findings.
- **Optional future enhancement** (GPT-5.5 review suggestion, non-blocking): a direct `PlaygroundFanoutService`
  end-to-end test asserting `ExtendedContextUnsupported` stays `Available=true` and affects `AllAgree`; the
  reviewer judged the existing substring test + code inspection sufficient.

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c4 |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

**Reviewer:** gpt-5.5 (rubber-duck, independent sub-agent `cs45-pvi`) — independent of the claude-opus-4.8 implementer
**Date:** 2026-07-05T06:41:52Z
**Outcome:** GO

Reviewed the merged CS45 implementation (content PR #159, squash `fa15868`) against the plan's Deliverables + Exit criteria at HEAD `085b71e`. All deliverables **match**; the reviewer independently ran `dotnet build` 0/0 and `dotnet test AuthzEntitlements.sln` 1677/0.

| Deliverable | Outcome | Rationale |
|---|---|---|
| D1 — fail-closed guard at the factory | match | `ExtendedContextGuardProvider` wraps non-capable providers; denies Actor/Delegation/BreakGlass with `ExtendedContextUnsupported`; covers the enforced + factory-resolved paths. |
| D2 — capability marker | match | `ISupportsExtendedAuthorizationContext` exists; `ReferenceDecisionProvider` declares it. |
| D3 — reconcile the Cerbos in-adapter guard | match | Removed; docs name the factory seam as authoritative (single guard). |
| D4 — shared regression tests | match | `ExtendedContextGuardTests` enumerates registered non-reference providers via both resolution paths; reference OBO permit/deny covered. |
| D5 — contract docs | match | `pdp-contract.md`, `adding-an-engine-adapter.md`, `cerbos-adapter.md` document the boundary + opt-in. |
| D6 — LEARNINGS entry | match | LRN-075 captures the fail-open + factory-guard remedy (flipped → applied at this close-out). |
| Exit criteria | match | No non-reference provider can fail open; a shared test enumerates all providers; the reference is unaffected; `dotnet build` 0/0, full suite 1677/0. |

**Test-coverage:** sufficient. **Overall outcome: GO** — no blocking findings.

Review-log row: `model: gpt-5.5` · `branch HEAD SHA: 085b71e94a2ea9ffafa07805b59ddf657e3c7789` · `R-round: R1` · `verdict: Go` · `evidence: PR #159`.
