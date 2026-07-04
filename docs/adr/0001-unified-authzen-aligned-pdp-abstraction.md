# 0001. Unified, AuthZEN-aligned PDP abstraction

Status: Accepted · Date: 2026-07-04 · Deciders: authz-and-entitlements team · Realized in: CS05

## Status

Accepted.

## Context

Fine-grained authorization for a regulated fintech workload has to answer contextual,
per-resource questions — *does this caller own this account?*, *is the checker different from
the maker?*, *is the amount over the approval threshold?* — that a coarse edge gateway
cannot. There are many candidate engines to answer them (a native rules engine, OPA/Rego,
Cedar, Casbin, OpenFGA, ASP.NET Core policies), and picking one up front would either lock
the codebase to a single vendor or scatter engine-specific calls across the application.

We needed one calling contract that is independent of the engine behind it, and a request /
response shape that is standards-aligned rather than bespoke, so an evaluator can reason
about it and adapters can map onto it faithfully.

## Decision

Introduce a single Policy Decision Point (PDP) seam,
[`IAuthorizationDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/IAuthorizationDecisionProvider.cs):
a stable `Name` plus `AccessDecision Evaluate(AccessRequest request)`. The shape mirrors the
OpenID **AuthZEN** Access Evaluation API — a request of **subject / action / resource /
context** in, and a self-explaining decision of **permit-or-deny + reasons + obligations**
out. `Decision.Deny = 0`, so the zero value fails closed. Every decision carries at least one
reason (`Reasons[0]` is the primary, machine-stable `Code`) and any obligations to honour on a
permit (e.g. `require_approval` for over-threshold transfers).

Calling code depends only on this seam; the concrete engine is resolved separately (see
[ADR 0003](0003-multi-engine-adapter-strategy-and-config-swap.md)).

## Consequences

**Positive**

- One calling contract for every engine: the app never names an engine, so engines are
  swappable without touching call sites.
- Standards alignment (AuthZEN) makes the request/response legible to external tooling and to
  the market comparison.
- Self-explaining decisions (reason codes + obligations) give every downstream layer — audit,
  explainability, enforcement — a stable key to match on.

**Negative / trade-offs**

- A shared shape is a lowest-common-denominator: an engine's richer native features must be
  mapped onto `AccessRequest` / `AccessDecision` or carried as obligations, which can lose
  nuance.
- `Evaluate` is synchronous; out-of-process engines must bridge their async client internally.
- The seam only spans authorization decisions; entitlements/quotas
  ([ADR 0005](0005-entitlements-via-openfeature-and-usage-metering.md)) are a separate contract.

## Alternatives considered

- **Call an engine SDK directly from the app.** Rejected: it couples business code to a
  vendor and makes an engine swap a code migration.
- **A bespoke internal decision DTO.** Rejected: AuthZEN gives the same expressiveness plus
  external legibility and a conformance target for free.
- **Per-domain ad-hoc checks (no PDP).** Rejected: it scatters security logic and defeats
  centralized audit and explainability.

## When to use / when not

- **Use** this seam for every fine-grained, contextual access decision in the system, and as
  the single integration point any new engine implements.
- **Not** for coarse OAuth scope enforcement at the edge (that stays at the gateway; the PDP
  only *re-checks* scopes as defence in depth), and **not** for commercial entitlement /
  quota gates, which have their own contract.
