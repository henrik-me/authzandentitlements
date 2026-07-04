# 0006. Fail-closed + audit-first decisioning

Status: Accepted · Date: 2026-07-04 · Deciders: authz-and-entitlements team · Realized in: CS04, CS05, CS13

## Status

Accepted.

## Context

In a regulated fintech system, the cost of *wrongly allowing* an action (a security or
compliance breach) is far higher than the cost of *wrongly denying* one (a retryable
inconvenience). Uncertainty is inevitable: a missing claim, an unreachable dependency, a
malformed payload, an unknown policy key. A system that defaults to "allow" on uncertainty, or
that makes decisions it cannot later prove, is unacceptable here. Two properties therefore had
to hold for *every* authorization and entitlement decision, at authoring time, across every
engine and gate — not as an afterthought.

## Decision

Make **fail-closed** and **audit-first** invariants of the decision path:

- **Fail closed everywhere.** Every uncertainty denies with a stable, non-sensitive reason,
  never permits and never lets an exception be the only signal. `Decision.Deny = 0` so the
  zero value is a deny. The reference engine denies on a missing/whitespace tenant
  (`TenantMismatch`), a non-pending target (`NotPending`), a maker == checker (`MakerEqualsChecker`),
  an unknown action (`UnknownAction`), and a missing delegated scope — and adapters must deny
  on any transport/timeout/parse/unknown-name error
  ([ADR 0003](0003-multi-engine-adapter-strategy-and-config-swap.md)). Transient infrastructure
  failures return a **503** (→ "unavailable, deny") distinct from a business deny, and never
  trust caller-supplied security attributes.
- **Audit first.** Every decision emits a structured, audit-ready event *at emission time*
  (deferred *ingestion* is fine, deferred emission is not). CS13 ships
  [`Audit.Service`](../../src/AuthzEntitlements.Audit.Service): an append-only, **hash-chained**
  Postgres store where each row binds the previous row's hash plus its own canonical content, so
  altering, reordering, inserting, or deleting any row breaks the chain and
  `GET /api/audit/verify` reports the first break. Decisions also fire OTel spans/metrics.
  Tampering is not prevented but made **evident**.

## Consequences

**Positive**

- Safe by default: an ambiguous or degraded state denies rather than leaks access.
- Every decision is provable after the fact, and any post-hoc alteration of the record is
  detectable — the compliance property regulated workloads require.
- A clear transient-vs-business distinction (503 vs 2xx deny) lets callers react correctly.

**Negative / trade-offs**

- Fail-closed can deny legitimate requests during a dependency outage (availability traded for
  safety) — an intentional bias.
- The hash chain is a single-writer, append-only store; it detects rather than prevents
  tampering, and its ordering constraint bounds write concurrency.
- Emitting an audit event on every decision adds per-request work and storage.

## Alternatives considered

- **Fail open / degrade to allow on dependency failure.** Rejected outright for a regulated
  fintech workload: it converts an outage into an authorization bypass.
- **Plain append-only log table (no hash chain).** Rejected: anyone with write access could
  edit or delete a row and erase the evidence undetectably.
- **Rely on database transaction logs / external SIEM only.** Rejected: those are operational,
  not a tamper-evident, self-verifying decision record with a stable per-decision schema.

## When to use / when not

- **Always** apply the fail-closed invariant on every authorization and entitlement gate, and
  emit an audit-ready decision event from every decision path — these are non-negotiable in this
  codebase.
- **Use** the 503-vs-business-deny distinction so fail-closed callers map infrastructure
  failures to "unavailable → deny" rather than mislabeling them.
- **Not** a reason to fail closed on *non-security* conveniences where a safe default differs;
  and the hash chain is for the decision audit trail, **not** a general-purpose application log.
