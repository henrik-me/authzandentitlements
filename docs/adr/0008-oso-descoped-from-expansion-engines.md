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

The CS05 `IAuthorizationDecisionProvider` seam an engine plugs into is fed by an engine that runs
either **in-process** or as a **self-hostable, production-grade server** pinned to a deterministic
image tag — the shape every other integrated engine takes (opt-in engines pin their tags, e.g. CS26
shipped `authzed/spicedb:v1.54.0` and `ghcr.io/cerbos/cerbos:0.53.0`). Oso, uniquely among the five,
offers neither: no in-process .NET binding, and no self-hostable **production** server — only a
vendor-designated *development* server and the paid, proprietary managed cloud.

## Decision

**De-scope Oso** — do not ship an Oso adapter. The CS26 feasibility research, re-verified during CS47
against primary sources (2026-07-05), found:

- **No maintained, in-process .NET/Polar library.** `github.com/osohq/oso` lists Python, Ruby,
  Java, Node.js, Go, and Rust — C#/.NET is absent, and `nuget.org/packages/Oso` returns 404. The
  classic OSS Oso library is additionally in **maintenance-only** mode, deprecated in favour of
  Oso Cloud.
- **The only .NET path is the managed Oso Cloud SDK.** The `OsoCloud` NuGet package (an HTTP REST
  client) talks to the hosted **Oso Cloud** API **or** to a local **dev-server**
  (`public.ecr.aws/osohq/dev-server`, also a downloadable native binary).
- **The self-hostable dev-server is a development tool, not a production server.** It *is* pinnable
  (versioned tags such as `:v1.2.3`, per Oso's "Pin Dev Server versions in CI" guidance) — so the
  earlier "`latest`-only / unpinnable" concern does **not** hold — but Oso scopes it to **local
  development and testing** ("before deploying to production"; ephemeral `.oso/` state; the on-disk
  format is explicitly not a stability guarantee). There is **no self-hostable production server**;
  production requires a **paid, proprietary Oso Cloud** account.

This conflicts with the self-host-first stance of
[ADR 0007](0007-self-host-first-authz-with-managed-optionality.md): Oso offers **no self-hostable
production path** — its production model is the paid, proprietary managed cloud, with self-hosting
limited to a development-only server. The gap is narrow and specific: the **Oso Cloud** SDK *does*
target .NET, and the dev-server *is* pinnable; what is missing is an **in-process .NET/Polar
library** or a **self-hostable, production-grade server** of the kind the other four opt-in engines
provide.

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

- **Ship the dev-server as a pinned container.** Technically feasible — the dev-server is pinnable
  (versioned tags) and the `OsoCloud` SDK targets .NET — but rejected: Oso designates the dev-server
  for **local development / testing only** (no production SLA, persistence, or support), so shipping
  it as an engine would misrepresent Oso as production-self-hostable when its only production path is
  the paid managed cloud. Acceptable solely for a throwaway lab demo (see *When to use / when not*).
- **Integrate paid Oso Cloud.** Rejected: it is a paid-cloud-only managed dependency on the decision
  hot path — off the self-host-first default of
  [ADR 0007](0007-self-host-first-authz-with-managed-optionality.md), which keeps managed offerings
  an opt-in per-engine drop-in, not a required path.

## When to use / when not

Re-evaluate Oso **only if** it ships **either**:

- **(a)** a maintained **in-process .NET/Polar library** targeting modern .NET; **or**
- **(b)** a **self-hostable, production-supported server** — i.e. the dev-server graduates to a
  supported production deployment, or an equivalent OSS server ships (not development-only, not
  paid-cloud-only).

Until one of those holds, Oso stays de-scoped. A maintainer who wants Oso purely for a throwaway lab
demo — accepting the development-only dev-server (pinned to a versioned tag) — reopens the
implement-vs-de-scope decision as a fresh CS.

## Sources

Availability verified as of **2026-07-05** against the sources below; Oso's packaging / product may
change — see the re-evaluation trigger.

- <https://www.osohq.com/>
- <https://www.osohq.com/docs/develop/local-dev/oso-dev-server> — the dev-server (development /
  testing scope; pinnable versioned tags via "Pin Dev Server versions in CI"; native binary)
- <https://github.com/osohq/oso> — the embedded-library language-SDK list (no C#/.NET)
- <https://www.nuget.org/packages/Oso> — returns 404 (no in-process .NET package)
- <https://www.nuget.org/packages/OsoCloud> — the managed-cloud .NET HTTP SDK (the only .NET path)
