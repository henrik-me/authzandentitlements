# 0007. Self-host-first authorization with managed optionality

Status: Accepted · Date: 2026-07-04 · Deciders: authz-and-entitlements team · Realized in: CS25 (analysis); CS05–CS10, CS20 (self-host + portability)

## Status

Accepted. The self-host-first posture and the portability seam it relies on are realized and
shipped; the Azure Container Apps target (CS27) is the chosen direction, **not yet shipped**, and is
marked forward-looking throughout.

## Context

The lab integrates several authorization engines behind one AuthZEN-aligned PDP seam
([ADR 0001](0001-unified-authzen-aligned-pdp-abstraction.md)), and nearly every managed authorization
service has a self-hostable open-source counterpart. We had to fix the lab's default **operational
posture**: run the engines ourselves, or reach first for a managed SaaS PDP. This is a total-cost-of-
ownership question, not only a feature one — it decides who carries the ops burden (server upgrades,
HA, the Postgres-backed tuple store, on-call) versus who pays a per-MAU / per-request fee for someone
else to carry it. It also had to survive the move to the cloud (CS27), where the available managed
options depend on which cloud you land on.

## Decision

Adopt **self-host-first authorization, with managed offerings as an optional per-engine drop-in**:

- **Self-host is the default, and in-process is the extreme of it.** Four engines (`reference`,
  `aspnet`, `casbin`, `cedar`) run **in-process** with zero extra infrastructure — no container,
  server, or store; two more (`opa`, `openfga`) run **self-hosted out-of-process** (a container each;
  `openfga` adds a shared Postgres). This is the shipped posture.
- **Managed is optional, not the default.** Every engine answers the same
  `IAuthorizationDecisionProvider` contract, selected by one `Pdp:Provider` config value, and CS20
  adds an RBAC→ReBAC translator plus a shadow / dual-run parity gate. Swapping a self-hosted engine
  for its managed equivalent — Auth0 / Okta FGA for `openfga`, AuthZed Cloud for SpiceDB, Amazon
  Verified Permissions for `cedar`, or Permit.io as a control plane over `opa` / `openfga` / `cedar` —
  is therefore a **config + parity-check** operation, not a rewrite.
- **Choose managed only when the ops burden outweighs the fee.** Per the
  [TCO analysis](../eval/managed-vs-selfhost-tco.md), for this lab's size the in-process engines are
  the lowest-TCO default (essentially nothing to run); managed wins when running a Postgres-backed
  ReBAC store — upgrades, HA, on-call — costs more than a per-MAU / per-request bill.
- **Cloud target is Azure Container Apps via `azd`** (`ARCHITECTURE.md`), realized in **CS27
  (forward — the chosen direction, not yet shipped)**. A load-bearing consequence: **Amazon Verified
  Permissions is AWS-only and is therefore excluded on Azure** — managed Cedar behavior on Azure comes
  from the in-process `cedar` engine, not from AVP.

## Consequences

**Positive**

- Lowest-TCO default for a lab this size: the four in-process engines cost essentially nothing to run
  and stay on the ordinary Docker-free, deterministic build / test gate.
- No vendor lock-in on the default path, and the managed entry / exit is a config + parity check
  rather than a rewrite, thanks to the shared seam
  ([ADR 0003](0003-multi-engine-adapter-strategy-and-config-swap.md)) and the CS20 translator.
- The posture is decided **per engine** and is reversible, and it is grounded in an explicit TCO
  framework (pricing meter, infra, ops burden, lock-in, hosting) instead of a sticker price.

**Negative / trade-offs**

- Self-host means the team owns the ops burden of the out-of-process engines — `opa` / `openfga`
  upgrades, HA, the `openfga` Postgres store, and on-call — and owns the adapter plus the shared
  `FintechRuleEvaluator` parity code for the in-process ones.
- The Azure / CS27 cost-shape (ACA compute + PostgreSQL Flexible Server + observability) is analyzed
  but **unverified** until CS27 actually ships.
- AVP being AWS-only removes one managed option on Azure, narrowing the managed-authz choices there to
  the four cloud-agnostic SaaS (Auth0 / Okta FGA, AuthZed Cloud, Oso Cloud, Permit.io).

## Alternatives considered

- **Managed-SaaS-first (buy, don't run).** Rejected as the default: it puts a per-request network hop
  and a per-MAU / per-request fee on the decision hot path, and — for AVP — would pin the cloud to
  AWS, conflicting with the Azure target. Kept as an **opt-in** for teams that would rather not run a
  Postgres-backed ReBAC store.
- **Self-host only (no managed path).** Rejected: it discards a legitimate lower-ops option for small
  teams and would waste the portability seam; managed stays a supported drop-in.
- **Kubernetes (AKS) as the default cloud host.** Rejected for this lab's size: Azure Container Apps
  is the lower-TCO serverless-container default; AKS is reserved for a hard requirement (advanced
  networking, custom operators, strict compliance).

## When to use / when not

- **Self-host** when check volume is high (a flat compute bill beats per-check fees), you have
  data-residency / air-gap needs, you want no lock-in, or you already operate the OSS engine. The
  **in-process** engines are the extreme: near-zero run cost and no network dependency, at the price
  of owning the policy code and redeploying to change it.
- **Managed** when the team is small, you want an uptime SLA and someone else on-call, and check
  volume is low-to-moderate so the per-MAU / per-request fee stays below the fully-loaded cost of
  running and patching the server.
- **Not** a mandate to standardize on a single engine or a single posture: the choice is per-engine,
  and the [comparison matrix](../eval/comparison-matrix.md) and the
  [TCO doc](../eval/managed-vs-selfhost-tco.md) are the inputs for each such decision.
