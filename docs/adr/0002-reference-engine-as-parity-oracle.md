# 0002. Reference engine as the parity oracle

Status: Accepted · Date: 2026-07-04 · Deciders: authz-and-entitlements team · Realized in: CS05

## Status

Accepted.

## Context

Once authorization is answered behind a single seam
([ADR 0001](0001-unified-authzen-aligned-pdp-abstraction.md)) that many engines can implement
([ADR 0003](0003-multi-engine-adapter-strategy-and-config-swap.md)), a hard question follows:
*how do we know two engines actually agree?* Without a single, unambiguous definition of the
correct answer, "engine A and engine B both look right" is not a proof — each engine could be
subtly wrong in a different direction and no test would catch it. We needed a canonical,
deterministic definition of the intended fintech decision that every adapter is measured
against.

## Decision

Ship an in-process
[`ReferenceDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs)
(`Name = "reference"`) as the **parity oracle**: the canonical, pure, deterministic
implementation of the fintech decision that deliberately encodes the same rules as `Bank.Api`.
Its ordered per-action checks — scope re-check, tenant isolation, role eligibility,
subject-is-maker, pending status, segregation of duties (checker ≠ maker), and the
approval-threshold obligation — define the intended answer.

Correctness for any other engine is defined as: return the **same decision and the same
primary reason code** as the reference across the
[`FintechScenarioCatalog`](../../src/AuthzEntitlements.Authz.Pdp/Catalog/FintechScenarioCatalog.cs)
(the fintech scenarios expressed once, dispatchable to any engine via
[`ScenarioCatalogRunner`](../../src/AuthzEntitlements.Authz.Pdp/Catalog/ScenarioCatalogRunner.cs)).
The reference engine is the default `Pdp:Provider`, so the deterministic answer is also what a
plain build/test run and a Docker-free environment get.

## Consequences

**Positive**

- A single source of truth for "correct": adapter parity is an objective, testable property,
  not a judgement call.
- Deterministic and DB-free, so the oracle runs in the ordinary build/test gate with no
  container or live server.
- Doubles as the safe default engine, so nothing depends on an external engine being up.

**Negative / trade-offs**

- The oracle is only as correct as its hand-encoded rules; a bug in the reference is a bug in
  the definition of correct, so it carries extra review weight.
- Parity is defined on decision + primary reason code (and obligations); it deliberately does
  not police an engine's richer native explanation beyond that.
- Keeping the reference in lockstep with `Bank.Api`'s enforced rules is a standing maintenance
  obligation.

## Alternatives considered

- **No oracle — cross-check engines against each other.** Rejected: mutual agreement is not
  correctness; shared blind spots go undetected.
- **Treat an external engine (e.g. OPA) as the source of truth.** Rejected: it makes the
  build/test gate depend on that engine and its availability, and picks a winner prematurely.
- **Golden expected-output fixtures only.** Rejected: a live, executable oracle also serves as
  the default engine and the shadow-run baseline, which static fixtures cannot.

## When to use / when not

- **Use** the reference as the baseline in every catalog-parity test and as the trusted side
  of a [shadow / dual-run](0003-multi-engine-adapter-strategy-and-config-swap.md) comparison.
- **Use** it as the default engine wherever determinism and zero external dependencies matter
  (CI, local dev, tests).
- **Not** as the production engine when a scenario genuinely needs a capability the reference
  does not model (e.g. relationship graphs — see
  [ADR 0004](0004-rebac-with-openfga-for-relationships.md)); there it is the oracle, not the
  answer.
