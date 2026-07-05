# 0008. Oso de-scoped from the expansion-engine set

Status: Accepted · Date: 2026-07-05 · Deciders: authz-and-entitlements team · Realized in: CS26 (feasibility research); CS47 (disposition)

## Status

Accepted. Oso is intentionally **excluded** from the expansion-engine adapter set — no Oso adapter,
NuGet pin, or container ships. The decision is dated and carries an explicit re-evaluation trigger
(see *When to use / when not*).

## Context

CS26 planned five expansion engines behind the unified PDP seam
([ADR 0001](0001-unified-authzen-aligned-pdp-abstraction.md)): SpiceDB, Cerbos, Keto, Oso, and
Topaz. SpiceDB and Cerbos shipped (PRs #134, #139); Keto and Topaz split to a separate follow-on
CS (CS46); Oso is dispositioned here.

The CS05 `IAuthorizationDecisionProvider` seam an engine plugs into requires an engine that runs
either **in-process** or as a **pinnable, self-hostable** container — the shape every other
integrated engine takes, and the basis of the repo's image-pinning-for-determinism convention
(opt-in engines pin their image tags — e.g. CS26 shipped `authzed/spicedb:v1.54.0` and
`ghcr.io/cerbos/cerbos:0.53.0`; `latest` is non-deterministic). Oso, uniquely among the five, offers
no such path.

## Decision

**De-scope Oso** — do not ship an Oso adapter. The CS26 feasibility research (verified 2026-07-04)
found:

- **No maintained, publicly-discoverable in-process .NET/Polar library.** `github.com/osohq/oso`
  lists Python, Ruby, Java, Node.js, Go, and Rust — C#/.NET is absent, and `nuget.org/packages/Oso`
  returns 404. The classic OSS Oso library is additionally in **maintenance-only** mode, deprecated
  in favour of Oso Cloud.
- **The only .NET path is Oso Cloud.** The `OsoCloud` NuGet SDK (`1.10.0`, net6.0→net10, HTTP REST)
  talks to the hosted **Oso Cloud** API **or** to a local **dev-server** image
  `public.ecr.aws/osohq/dev-server:latest`.
- **The dev-server is `latest`-only** (no pinned semver tag confirmed from public docs) and is
  **explicitly a development server** — no SLA, persistence, or production support. Production
  requires a **paid Oso Cloud** account.

This conflicts with two established repo postures: the **image-pinning-for-determinism** convention
(`latest` is non-deterministic) and the self-host-first stance of
[ADR 0007](0007-self-host-first-authz-with-managed-optionality.md) — Oso offers no pinnable,
self-hostable production path. Note the gap is narrow: the **Oso Cloud** SDK *does* target .NET; what
is missing is an in-process or pinnable-self-hostable path.

## Consequences

**Positive**

- The eval docs stay honest: Oso reads as an **evaluated, intentional exclusion**, not a silent gap
  or an incomplete deliverable.
- The deterministic, Docker-free **default** build / test path is unaffected — nothing is added to
  run.
- The `IAuthorizationDecisionProvider` seam is unchanged; the expansion set (SpiceDB + Cerbos
  shipped, Keto + Topaz planned) carries the relationship- and policy-engine coverage.

**Negative / trade-offs**

- The lab has **no single-language Polar option** spanning RBAC / ABAC / ReBAC in one DSL — the one
  thing Oso is unusually good at.
- The decision is **dated** and may go stale if Oso changes its packaging — mitigated by the
  re-evaluation trigger below.

## Alternatives considered

- **Ship the `latest` dev-server anyway.** Rejected: it is unpinnable (`latest`-only) and
  development-only (no SLA / persistence / production support), which breaks the image-pin
  determinism convention every other opt-in engine follows.
- **Integrate paid Oso Cloud.** Rejected: it is a paid-cloud-only managed dependency on the decision
  hot path — off the self-host-first default of
  [ADR 0007](0007-self-host-first-authz-with-managed-optionality.md), which keeps managed offerings
  an opt-in per-engine drop-in, not a required path.

## When to use / when not

Re-evaluate Oso **only if** it ships **either**:

- **(a)** a maintained **in-process .NET/Polar library** targeting modern .NET; **or**
- **(b)** a **pinnable, self-hostable production server** image — not `latest`-only, not
  paid-cloud-only.

Until one of those holds, Oso stays de-scoped. A maintainer who wants Oso for a lab-only demo
despite these constraints (e.g. accepting the `latest` dev-server) reopens the implement-vs-de-scope
decision as a fresh CS.

## Sources

Pricing / availability verified as of **2026-07-04** against the sources below; Oso's
packaging / product may change — see the re-evaluation trigger.

- <https://www.osohq.com/>
- <https://www.osohq.com/docs>
- <https://github.com/osohq/oso> — the language-SDK list (no C#/.NET)
- `OsoCloud` NuGet package (`1.10.0`, net6.0→net10, HTTP REST) — the only .NET path
- `public.ecr.aws/osohq/dev-server:latest` — the `latest`-only, development-only local image
