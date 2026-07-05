# Managed-vs-self-host TCO + cloud move

Part of the Phase 6 evaluation lab (CS25). This document analyses the **total cost of ownership
(TCO)** and operational trade-offs between **managed** authorization services and **self-hosting**
the open-source engines this lab already runs, then covers **what changes when the lab moves to the
cloud (Azure)** — the input that feeds the Azure deployment work in CS27.

It is a companion to the performance work in
[performance-benchmarks.md](performance-benchmarks.md): that document measures *how fast* each engine
answers; this one measures *what it costs to own* the engine over its lifetime.

> **Read this for the model, not the number.** Vendor list prices, tier limits, and SLA percentages
> are **indicative and change frequently** — several of the vendors below publish **no public price
> at all** and quote only on request ("contact sales"). Treat every dollar figure and threshold here
> as a snapshot that may already be stale; each is anchored to a source with an access date of
> **2026-07-04** in [Sources](#sources). The **durable signal is the pricing _model_ and the cost
> _drivers_** — what you are metered on, and who carries which operational burden — not any one
> quarter's price sheet. This mirrors the benchmark doc's honesty caveat: absolute figures are
> environment- and time-specific; the trend and the shape are what last.

## What this lab actually ships (the baseline)

The analysis is grounded in the lab's real stack, not a generic authorization product. The unified
AuthZEN-aligned PDP (`AuthzEntitlements.Authz.Pdp`, CS05) runs pluggable engines behind one
`IAuthorizationDecisionProvider` contract (see `ARCHITECTURE.md`):

| Engine (`Pdp:Provider`) | Kind | Runs as | Backing store | Status |
|---|---|---|---|---|
| `reference` | Hand-written oracle | In-process (no container) | none | Shipped (CS05) |
| `aspnet` | ASP.NET Core native | In-process (no container) | none | Shipped (CS06) |
| `casbin` | Casbin.NET | In-process (no container) | none | Shipped (CS06) |
| `cedar` | Cedar (`MonoCloud.Cedar`) | In-process (no container) | none | Shipped (CS09) |
| `opa` | OPA / Rego | Out-of-process container | none (policy bundle) | Shipped (CS08) |
| `openfga` | OpenFGA (ReBAC / Zanzibar) | Out-of-process container | Postgres (`openfga` DB) | Shipped (CS07) |
| SpiceDB, Cerbos, Keto, Topaz | expansion | Out-of-process | varies | SpiceDB + Cerbos shipped (CS26); Keto + Topaz planned (CS46) |

Oso was evaluated as a sixth expansion engine but **de-scoped** — see
[ADR 0008](../adr/0008-oso-descoped-from-expansion-engines.md).

Two facts drive everything below:

- The **four in-process engines cost essentially nothing to _run_** — they ship inside the
  PDP / Bank.Api process, add no container, no server, and no store. Their cost is **engineering**:
  you own the adapter and the shared `FintechRuleEvaluator` parity code.
- The **out-of-process engines** (`opa`, `openfga`) need a running server; `openfga` also needs
  Postgres. Those are exactly the components a *managed* offering takes off your hands.

## TCO framework: the dimensions

TCO for an authorization engine is not a single price — it is a bundle of recurring fees,
infrastructure, and the human cost of keeping the thing running. Every option is compared on:

1. **Pricing model / meter** — what you are charged *per*: monthly active users (MAU), authorization
   checks / requests, stored relationship **tuples**, seats, a **flat** tier, pure **usage**, or a
   **custom** enterprise quote. The meter, not the sticker, is what scales your bill.
2. **Infrastructure cost** — compute + storage + network you provision yourself (zero for a pure SaaS
   check API; a container + Postgres for self-hosted OpenFGA).
3. **Operational burden** — who runs upgrades, HA / replication, backups, security patching, scaling,
   and **on-call**. This is the cost that hides in an engineer's calendar rather than on an invoice.
4. **Latency & performance ownership** — an in-process engine is sub-millisecond and yours to tune; a
   SaaS check API adds a network hop you do not control (see the benchmark doc for the in-process
   floor).
5. **SLA & support tier** — dev / free tiers typically carry **no** SLA and community support only; a
   contractual uptime SLA is usually an enterprise (contact-sales) feature.
6. **Data residency & compliance** — where tuples / policies / decisions live, and whether you can pin
   a region. (The full SOX / PCI-DSS / GDPR mapping is CS22's remit; this doc only flags where
   residency is a *cost* driver.)
7. **Vendor lock-in / exit cost** — can you leave with your model intact? An OSS-engine SaaS
   (OpenFGA / SpiceDB) has a self-host exit; a proprietary policy language (Polar) or service API
   (AVP) raises the exit cost.
8. **.NET / AuthZEN fit** — is there a first-class .NET client, and does it slot behind the lab's
   AuthZEN-aligned PDP without bespoke glue?
9. **Maturity** — how proven the engine *and* the managed operation are.

## Managed offerings

Each subsection covers the pricing **model**, the ops burden removed, the lock-in profile, cloud /
region availability, and how it maps to the lab's engines.

### Auth0 / Okta FGA (managed OpenFGA)

- **What it is:** Okta's managed cloud of **OpenFGA**, the Zanzibar-style ReBAC engine Okta / Auth0
  created and donated to the CNCF. Maps directly to the lab's **`openfga`** engine (CS07) — same
  check API, same tuple model, same `OpenFga.Sdk` .NET client.
- **Pricing model:** metered primarily on **monthly active users (MAU)**, with per-authorization-store
  caps on **relationship tuples** and **check throughput** (checks / sec). A free developer tier
  exists for evaluation (no production SLA, community support); production and higher
  tuple / store / region limits move you up the paid tiers, and the top tier is **contact-sales** with
  a negotiated uptime SLA.
- **Ops burden removed:** OpenFGA server upgrades, HA, the tuple store (Postgres), scaling, and
  on-call — the exact components the lab runs itself in the `openfga` container + `openfga` Postgres
  DB.
- **Lock-in: low-to-moderate.** The data model and API are open-source OpenFGA, so the exit path is
  "self-host OpenFGA" and you keep your model and tuples. Only the managed surface (console, MAU
  metering) is left behind.
- **Cloud / region:** SaaS with US / EU / AU regions; **cloud-agnostic** (reachable from Azure).

### AuthZed Cloud (managed SpiceDB)

- **What it is:** AuthZed's managed **SpiceDB**, the other major Zanzibar-style ReBAC engine. Maps to
  the lab's **shipped** SpiceDB expansion engine (CS26) — a head-to-head with `openfga`. .NET talks to
  it over gRPC (`Authzed.Net`).
- **Pricing model:** two shapes. **AuthZed Cloud** is **serverless, usage / resource-based**
  pay-as-you-go (you pay for what you consume, no cluster to size). **AuthZed Dedicated** is
  **single-tenant reserved capacity** (reserved vCPU, isolation, private networking) and is
  **contact-sales**. SpiceDB itself is free OSS if you self-host.
- **Ops burden removed:** SpiceDB upgrades, the datastore, HA / replication, multi-region, and
  scaling.
- **Lock-in: low-to-moderate** — same logic as OpenFGA: the engine is OSS, so the exit is
  self-hosted SpiceDB with your schema and relationships intact.
- **Cloud / region:** SaaS (Cloud) or dedicated single-tenant; **cloud-agnostic**.

### Oso Cloud (managed Oso)

- **What it is:** managed **Oso**, whose policies are written in the **Polar** language (a different
  model from the RBAC / ReBAC / Cedar engines). Oso was **evaluated and de-scoped** from the lab's
  expansion engines (see [ADR 0008](../adr/0008-oso-descoped-from-expansion-engines.md)); Oso Cloud
  is an HTTP API, so it is callable from .NET even though Oso's .NET story is thinner than the ReBAC
  engines'.
- **Pricing model:** a free **developer** tier for low volume, then **per-authorization-request**
  usage tiers (priced per bundle of requests) layered with MAU thresholds; the top tier is
  contact-sales with a higher SLA, more regions, and longer log retention.
- **Ops burden removed:** running the Oso Cloud decision / "facts" service, scaling the store, and
  reverse-index / list queries.
- **Lock-in: moderate-to-high** — Polar and the Oso "facts" model are Oso-specific; there is no
  drop-in OSS server to fall back to the way OpenFGA / SpiceDB self-host, so an exit is a policy
  rewrite.
- **Cloud / region:** SaaS, multiple regions; **cloud-agnostic**.

### Permit.io (managed multi-engine)

- **What it is:** a managed **control plane over OPA + OpenFGA + Cedar** — i.e. it wraps exactly the
  engines the lab already runs (`opa`, `openfga`, `cedar`). Ships a **.NET SDK** and, importantly, a
  **local PDP sidecar**: the decision container runs next to your app and syncs policy / data from
  Permit's cloud, so the hot-path check is in-cluster (not a cross-internet call).
- **Pricing model:** metered on **MAU** plus **tenant** count. A **perpetual free community tier**
  (small MAU / tenant caps) covers PoCs; paid tiers raise MAU / tenant / environment limits and add
  SSO, support, and compliance; the top tier is contact-sales (unlimited, on-prem / multi-cloud, SLA).
- **Ops burden removed:** policy-authoring UI / GitOps, decision-log aggregation, and engine
  packaging. Because the PDP is a sidecar you still run, uptime is a **shared** burden: Permit owns
  the control plane, you own the sidecar's placement.
- **Lock-in: moderate.** The underlying engines are OSS, but the policy-authoring model, the
  data-sync protocol, and the management UI are Permit-specific.
- **Cloud / region:** **cloud-agnostic** SaaS control plane + a sidecar you host anywhere (including
  Azure) — the best architectural fit for keeping decisions in-cluster on Azure.

### Amazon Verified Permissions (managed Cedar) — AWS-only

- **What it is:** AWS's managed **Cedar** service. Maps directly to the lab's **`cedar`** engine (CS09,
  in-process `MonoCloud.Cedar`); AVP already appears as the managed / cloud Cedar option in the CS09
  adapter doc. .NET uses `AWSSDK.VerifiedPermissions`.
- **Pricing model:** **pure pay-as-you-go, per-authorization-request** — no MAU meter, no seats, no
  minimum / commit. Single `IsAuthorized` / `IsAuthorizedWithToken` calls are billed per million
  requests (indicatively **~$5 / million** after a June-2025 price reduction — verify current), batch
  authorization is tiered cheaper per request, and policy-management calls are billed separately. This
  is the most usage-transparent model of the five.
- **Ops burden removed:** essentially all of it — there is no server, store, or scaling to run; Cedar
  evaluation is AWS-operated.
- **Lock-in:** the **policy language is portable** (Cedar is Apache-2.0 OSS, and the lab evaluates it
  in-process), **but the _service_ is not** — AVP's API, policy store, and identity-source integration
  are AWS-proprietary.
- **Cloud / region: AWS-only.** AVP runs solely in AWS regions. **This is the decisive constraint for
  the Azure move (CS27): AVP is not available on Azure at all.** You can keep Cedar *policies* and run
  them in-process (the lab's `cedar` engine) or on another Cedar host, but you cannot consume AVP as a
  managed service from Azure.

## Self-hosted OSS TCO (what the lab does today)

Self-hosting is the lab's default: every engine above runs locally under Aspire with **zero per-check
vendor fee**. The cost moves from a vendor invoice to **your infrastructure bill + your engineering
time**.

**In-process engines (`reference`, `aspnet`, `casbin`, `cedar`) — near-zero run cost.**
They compile into the PDP / Bank.Api process: no container, no server, no store, no network hop. The
TCO is almost entirely **engineering** — authoring and maintaining the adapter + the shared
`FintechRuleEvaluator` parity, and keeping the embedded library (`Casbin.NET`, `MonoCloud.Cedar`)
patched. For many workloads this is the cheapest *and* fastest option (sub-millisecond, per the
benchmark doc); its ceiling is operational, not financial — policies live in your deployment, so a
policy change is a redeploy, and there is no central multi-service policy plane.

**Out-of-process engines (`opa`, `openfga`) — real but modest infra + ops.** Cost drivers:

- **Compute:** one small container per engine (`openpolicyagent/opa`, `openfga/openfga`); they scale
  horizontally and are stateless (`opa`) or thin over Postgres (`openfga`).
- **Store:** `openfga` needs Postgres — in the lab, the shared Postgres resource's `openfga` logical
  DB. `opa` needs none (it loads a policy bundle).
- **Engineering (the dominant line):** upgrades (pinned `opa:1.18.2-static`, `openfga:v1.18.1`
  today), HA / replication, backups of the tuple store, monitoring, and **security patching** — the
  burden a managed offering removes.

**Shared backing store.** The lab runs a single Postgres resource exposing six logical DBs (`bank`,
`openfga`, `entitlements`, `governance`, `audit`, and the optional `unleash` for the CS10 feature-flag
provider). Self-hosting authz does **not** add a new database product — it adds one logical DB plus its
backup / HA responsibility.

**Portability is designed in (it lowers exit cost).** CS20 shipped an RBAC→ReBAC translator and a
dual-run / shadow parity gate, and the PDP is AuthZEN-aligned. That means the lab can move a policy
between engines — and therefore between *self-host and managed* — with a parity check rather than a
rewrite, which is the single biggest lever on lock-in cost.

## Comparison at a glance

Qualitative only — **Low / Med / High** plus a phrase; no dollar amounts (see the caveat). "You" =
the burden stays with the lab; "Vendor" = the offering removes it.

| Option | Meter (pricing model) | Infra cost (you) | Ops burden (you) | Lock-in | Lab engine it maps to |
|---|---|---|---|---|---|
| Self-hosted OSS, in-process | none (your compute) | **Low** — no extra container / store | **Med** — you own adapter + patches | **Low** | `reference` / `aspnet` / `casbin` / `cedar` (shipped) |
| Self-hosted OSS, out-of-process | none (your compute) | **Med** — container(s) + Postgres | **High** — upgrades / HA / backups / on-call | **Low** | `opa`, `openfga` (shipped) |
| Auth0 / Okta FGA | per-MAU + tuple / check caps | **Low** | **Low** — vendor runs OpenFGA | **Low–Med** (OSS engine) | `openfga` |
| AuthZed Cloud | usage / resource (Cloud) or reserved (Dedicated) | **Low** | **Low** | **Low–Med** (OSS engine) | SpiceDB (shipped, CS26) |
| Oso Cloud | per-request + MAU tiers | **Low** | **Low** | **Med–High** (Polar) | Oso (evaluated, de-scoped — ADR 0008) |
| Permit.io | per-MAU + tenants | **Low–Med** — you host the sidecar | **Low–Med** — shared (sidecar) | **Med** | `opa` + `openfga` + `cedar` |
| Amazon Verified Permissions | per-request (pay-as-you-go) | **Low** | **Lowest** — no server at all | **Med** (service) / **Low** (Cedar policy) | `cedar` — **AWS-only** |

## Moving to the cloud: Azure (feeds CS27)

The lab runs local-first under Aspire; CS27 deploys it to **Azure via `azd`** (already the stated
target in `ARCHITECTURE.md`). What changes, component by component:

### Hosting the services + engine containers

- **Azure Container Apps (ACA)** is the natural first target: a **serverless container** platform (no
  cluster / node management, scale-to-zero, KEDA autoscaling, Dapr) that `azd` templates directly. The
  lab's services (`bank-api`, `edge-gateway`, `entitlements-service`, `governance-service`,
  `audit-service`, `authz-pdp`, `bank-web`) and the **out-of-process engine containers** (`opa`,
  `openfga`) map cleanly to ACA apps. The in-process engines need **no** ACA app — they ride inside
  the service that hosts them, so they cost nothing extra on Azure.
- **Azure Kubernetes Service (AKS)** is the alternative when you need full Kubernetes control —
  advanced networking, custom operators, strict compliance, or GPU. It adds cluster ops; **AKS
  Automatic** narrows that gap. For this lab's size, **ACA is the lower-TCO default**; reach for AKS
  only if a hard requirement forces it.

### The Postgres store

- Replace the local Postgres **container** with **Azure Database for PostgreSQL Flexible Server**. It
  is billed per-second on **vCores + provisioned storage + backup**, supports burstable compute,
  stop / start, and **zone-redundant HA**. The lab's six logical DBs (including `openfga`) move onto
  it with connection-string changes only — EF Core 10 + Npgsql are unchanged.
- This is the single largest recurring Azure line item for the **self-host** authz path, and it is the
  cost a managed ReBAC offering (Auth0 / Okta FGA, AuthZed Cloud) folds into its per-MAU / usage price.

### Observability

- The lab already exports **OTLP** to a `grafana/otel-lgtm` container (OTel Collector + Prometheus +
  Tempo + Loki + Grafana), gated on `OTEL_EXPORTER_OTLP_ENDPOINT`. That indirection is the whole
  point: on Azure you **re-point the endpoint**, with no service-code change.
- Two Azure-native options: **Azure Monitor** (metrics / logs / traces; billed on GB ingested +
  retention) with **Azure Managed Grafana** (billed per active user + workspace hour) rendering the
  existing dashboards, *or* keep running the self-hosted `otel-lgtm` / Grafana stack as a container on
  ACA. The first lowers ops; the second keeps the CS12 / CS24 dashboards
  (`pdp-performance.json`, etc.) byte-for-byte and avoids per-GB ingest cost. Either works because the
  emit side is standard OTLP.

### Managed authorization on Azure — the AVP constraint

- **Amazon Verified Permissions is AWS-only and is therefore _not_ an option on Azure.** If CS27 wants
  managed Cedar behavior on Azure, the path is the lab's **in-process `cedar` engine** (zero extra
  infra) or self-hosting a Cedar host — not AVP.
- The other four are **cloud-agnostic SaaS** and work alongside Azure:
  - **Auth0 / Okta FGA**, **AuthZed Cloud**, and **Oso Cloud** are reached over the network from
    ACA / AKS. Budget for a **cross-internet hop + egress** on the decision hot path, and check
    data-residency (pick a vendor region near your Azure region).
  - **Permit.io** is the best architectural fit: its **PDP sidecar runs _in_ your Azure environment**
    (an ACA app next to `authz-pdp`), so the hot-path check stays in-region while Permit's cloud only
    handles control-plane sync.
- Azure itself has **no first-party equivalent to AVP** (Microsoft Entra covers identity + coarse-
  grained authorization, not a ReBAC / ABAC PDP). So on Azure the realistic managed-authz choices are
  the four cloud-agnostic SaaS above; everything else is self-host.

### Net cost-shape on Azure

- **Self-host on Azure:** ACA compute for the services + `opa` / `openfga` + **Flexible Server** for
  Postgres + observability. Predictable, capacity-based, no per-check fee; you still own
  upgrades / HA.
- **Managed on Azure:** drop the engine container(s) and (for OpenFGA / SpiceDB) the Postgres burden;
  pay a per-MAU / per-request fee instead; add cross-cloud latency unless the vendor runs a local
  sidecar (Permit.io) or you accept the hop.

## Guidance: when to choose what

- **Choose managed when:** the team is small, you want an uptime SLA and someone else on-call, you'd
  rather not run a Postgres-backed ReBAC store, and your check volume is low-to-moderate (per-MAU /
  per-request fees stay below the fully-loaded cost of running + patching the server). For **Cedar on
  AWS**, AVP's per-request, no-minimum model is hard to beat; for **ReBAC**, Auth0 / Okta FGA or
  AuthZed Cloud remove the tuple-store burden.
- **Choose self-host when:** check volume is high (a flat compute bill beats per-check fees at scale),
  you have **data-residency / air-gap** requirements, you want **no vendor lock-in**, or you are
  **already operating the OSS engine** (the lab already runs `opa` and `openfga`). The **in-process**
  engines are the extreme of this: near-zero run cost and no network dependency, at the price of
  owning the policy code and redeploying to change it.
- **The hybrid path this lab is on:** in-process engines as the fast, embedded default; self-hosted
  `opa` / `openfga` containers for the richer policy / ReBAC models; a managed offering as a drop-in
  for a specific engine when the ops burden outweighs the fee. The CS20 portability translator +
  AuthZEN alignment + the shadow / dual-run parity gate keep that swap low-risk — you move an engine
  between self-host and managed with a parity check instead of a rewrite.

## Cross-references

This TCO / economics view is one axis of the CS23–CS25 evaluation lab; the companion documents cover
the other axes:

- [Comparison matrix](comparison-matrix.md) — the feature / technical comparison of the engines
  (models, consistency, latency, reverse-index, policy language, testability, auditability, ops
  burden, .NET support, AuthZEN alignment, licensing). It is the complement to this document's
  cost / economics view.
- [Market survey](market-survey.md) — the broad ReBAC, policy-engine, and entitlements landscape,
  with per-tool strengths, weaknesses, and when-to-use notes.
- [ADR 0007 — Self-host-first authorization with managed optionality](../adr/0007-self-host-first-authz-with-managed-optionality.md)
  — the architecture decision this analysis grounds.

## Sources

Accessed **2026-07-04**. Prices and limits are indicative and change frequently — confirm against
each vendor's live pricing page before relying on a figure.

- **Auth0 / Okta FGA** (managed OpenFGA): <https://auth0.com/fine-grained-authorization>,
  <https://docs.fga.dev/subscription-plans>, <https://auth0.com/pricing>. OpenFGA (OSS engine):
  <https://openfga.dev/>.
- **AuthZed Cloud / Dedicated** (managed SpiceDB): <https://authzed.com/pricing>,
  <https://authzed.com/products/authzed-dedicated>. SpiceDB (OSS): <https://authzed.com/spicedb>.
- **Oso Cloud**: <https://www.osohq.com/pricing>.
- **Permit.io**: <https://www.permit.io/pricing>, <https://www.permit.io/blog/permit-new-pricing-model>.
- **Amazon Verified Permissions** (AWS-only) + the June-2025 price reduction:
  <https://aws.amazon.com/verifiedpermissions/pricing/>,
  <https://aws.amazon.com/about-aws/whats-new/2025/06/amazon-verified-permissions-reduces-price/>.
  Cedar (OSS policy language): <https://www.cedarpolicy.com/>.
- **Azure** — Container Apps: <https://azure.microsoft.com/products/container-apps>; AKS:
  <https://azure.microsoft.com/products/kubernetes-service>; Database for PostgreSQL Flexible Server:
  <https://azure.microsoft.com/products/postgresql>; Azure Monitor:
  <https://azure.microsoft.com/products/monitor>; Azure Managed Grafana:
  <https://azure.microsoft.com/products/managed-grafana>.
