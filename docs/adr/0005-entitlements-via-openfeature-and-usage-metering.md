# 0005. Entitlements via OpenFeature + usage metering

Status: Accepted · Date: 2026-07-04 · Deciders: authz-and-entitlements team · Realized in: CS10

## Status

Accepted.

## Context

Authorization ("*may* this subject do this?") is not the same question as commercial
entitlement ("*is this tenant's plan licensed for this, and are they within their quota?*").
Plan tiers, per-module licensing (wire / FX / treasury), seat limits, and usage quotas are
product/billing concerns that change on a different axis from security policy and must be
gated per tenant plan. Folding them into the PDP would overload the authorization seam with
billing state; hard-coding feature checks would make plan changes a code change and lock the
system to one flag vendor.

## Decision

Model commercial entitlements as a **separate** concern from authorization, in an
`Entitlements.Service`, with feature gates behind the vendor-neutral **OpenFeature** SDK:

- Plans + modules, seat limits, and feature gates are evaluated through OpenFeature so the flag
  provider is swappable. The default is an **in-memory** provider (deterministic, no external
  dependency); a real **Unleash** provider is **opt-in** via configuration, with the Unleash
  container wired in the Aspire AppHost using `WithExplicitStart()` — mirroring the
  Docker-free-default discipline in [ADR 0003](0003-multi-engine-adapter-strategy-and-config-swap.md).
- Usage **quotas** are metered with a lightweight Postgres `UsageCounter` plus an OTel meter
  (`AuthzEntitlements.Entitlements`); quota-consume uses `xmin` optimistic concurrency with a
  retry loop. (Full OpenMeter integration is explicitly deferred to CS27.)
- Enforcement hooks live in `Bank.Api` (e.g. `POST /api/transactions` → wire-module /
  high-value-feature / monthly-quota gates), fail closed, and emit audit-ready
  `EntitlementDecision` events consumed by the audit pipeline
  ([ADR 0006](0006-fail-closed-and-audit-first-decisioning.md)).

## Consequences

**Positive**

- Clean separation: billing/plan logic evolves independently of security policy.
- Vendor-neutral flags — the flag backend swaps by config with no app-code change, and the
  in-memory default keeps builds/tests deterministic and Docker-free.
- Quota metering emits OTel metrics and audit-ready decision events out of the box.

**Negative / trade-offs**

- Two decision surfaces (PDP + entitlements) mean a request may consult both; callers must
  compose them correctly.
- The shipped metering is intentionally lightweight; full usage-metering (OpenMeter) is
  deferred, so this is not a complete billing/rating system yet.
- Hard capacity caps (seats) need careful concurrency handling — see the repository's
  advisory-lock convention — beyond what a naïve counter provides.

## Alternatives considered

- **Bind directly to one flag vendor's SDK (e.g. Unleash) everywhere.** Rejected: it couples
  the app to a vendor; OpenFeature keeps the provider swappable and still allows Unleash opt-in.
- **Model entitlements as authorization policy in the PDP.** Rejected: it conflates billing
  state with security decisions and overloads the AuthZEN seam.
- **Ship full OpenMeter metering now.** Deferred (CS27): the lightweight Postgres + OTel meter
  satisfies the plan-gating exit criteria without the added surface.

## When to use / when not

- **Use** the entitlements service + OpenFeature gates for plan-tier, module-licensing, seat,
  and quota decisions, and enforce them at the API boundary with fail-closed hooks.
- **Use** the in-memory provider for tests/local dev; enable the Unleash provider only where a
  real flag-management backend is warranted.
- **Not** for security/authorization decisions (use the PDP —
  [ADR 0001](0001-unified-authzen-aligned-pdp-abstraction.md)); **not** as a full billing/rating
  engine (metering is deliberately lightweight pending CS27).
