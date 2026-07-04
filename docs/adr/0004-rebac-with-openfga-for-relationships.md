# 0004. ReBAC via OpenFGA for relationships, hierarchy, and delegation

Status: Accepted · Date: 2026-07-04 · Deciders: authz-and-entitlements team · Realized in: CS07, CS20

## Status

Accepted.

## Context

Flat role-based access control (RBAC) answers *"does this subject's role grant this
permission?"* well, but a fintech workload also asks relationship questions a role list cannot
structurally express: *does this subject **own** this account?*, *is this the relationship
manager **for** this customer?*, *does this branch manager inherit authority over this
**region**?*, *has authority been **delegated** to this agent?* Encoding those as ever-more-
granular roles explodes the role set and still cannot represent per-object ownership or derived
hierarchy paths.

## Decision

Adopt **relationship-based access control (ReBAC)** using **OpenFGA** (a Zanzibar-style engine)
as an adapter behind the PDP seam, for exactly the relationship dimension flat RBAC cannot
express. Two complementary pieces ship:

- A hand-authored CS07
  [`RebacModel`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/RebacModel.cs) with
  types `user`, `region`, `branch`, `customer`, `account` and genuine relationships —
  account ownership, relationship-manager → customer (`can_view`), branch/region hierarchy
  (a branch manager inherits `region.manager`), and delegation (`account.delegate`) —
  resolved via `computedUserset` / `tupleToUserset`. The
  [`OpenFgaProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/OpenFgaProvider.cs)
  supports forward checks *and* reverse-index queries ("who can view account X" / "what can
  user Y access").
- A CS20 mechanical **RBAC → ReBAC translator**
  ([`RbacToRebacTranslator`](../../src/AuthzEntitlements.Authz.Pdp/Migration/RbacToRebacTranslator.cs))
  implementing the textbook "roles as usersets" pattern: each role becomes a userset, each
  grant becomes one tuple. It is pure and deterministic (byte-identical `ModelJson`, stably
  ordered tuples) and ships an in-process parity resolver that proves the translation decides
  identically to the source RBAC across the full user × permission grid — so an existing RBAC
  policy migrates with **zero regression on day one**, and the richer relationship value is
  added on top afterward.

OpenFGA is an opt-in container ([ADR 0003](0003-multi-engine-adapter-strategy-and-config-swap.md));
the deterministic default is undisturbed.

## Consequences

**Positive**

- Ownership, hierarchy, and delegation become first-class and queryable in both directions,
  including reverse-index "who can access X" — impossible to answer cleanly from a flat role
  list.
- The mechanical translator de-risks migration: prove parity in-process, then layer real
  relationships on top.

**Negative / trade-offs**

- A relationship graph plus tuple maintenance is more operational surface than a static role
  table, and (for the live engine) another service to run.
- The translation is faithful **only on the pure role → permission dimension**; contextual
  ABAC rules (scope, tenant, maker, pending, threshold, SoD) are deliberately *not* smuggled
  into tuples — they stay in the ABAC-capable engines.
- Relation identifiers are constrained (OpenFGA's `^[a-z][a-z0-9_]{0,62}$`); the sanitizer
  fails closed on any name it cannot represent unambiguously.

## Alternatives considered

- **Encode relationships as more RBAC roles.** Rejected: role explosion, and per-object
  ownership / derived hierarchy still cannot be expressed.
- **A different Zanzibar-style engine (e.g. SpiceDB).** Considered; OpenFGA was selected for
  this integration — see the [market survey](../eval/market-survey.md) for the broader
  landscape. This ADR records the choice, not a claim that alternatives are unusable.
- **Bespoke SQL joins for ownership/hierarchy.** Rejected: reinvents a relationship engine,
  loses the reverse-index and derived-path semantics, and scatters authorization logic.

## When to use / when not

- **Use** ReBAC/OpenFGA when the decision depends on *relationships* — ownership, org
  hierarchy, delegation — or when you need reverse-index "who can access X" queries.
- **Use** the translator to migrate an existing flat RBAC policy losslessly before enriching it.
- **Not** for purely contextual ABAC rules (tenant, amount threshold, maker-checker), which
  belong in the ABAC-capable engines; **not** where flat RBAC already suffices and the extra
  operational surface is unjustified.
