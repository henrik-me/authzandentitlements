# Entitlements, metering, and feature-flag platforms

A market-survey deep-dive of the **commercial entitlements + feature-flag** ecosystem that sits
*next to* — but is distinct from — the fine-grained authorization engines surveyed elsewhere.
These products answer a different question than a Policy Decision Point (PDP): not "may this
subject perform this action on this resource?" but "what has this *tenant* paid for, how much have
they *used*, and is this *feature* turned on for them?" The distinction matters, because
conflating billing/packaging state with an access decision is a fail-open hazard (see
[CONVENTIONS.md](../../../CONVENTIONS.md) "Fail-closed authorization + entitlements").

This document groups six representative platforms into four categories:

- **Usage metering / metered billing** — OpenMeter.
- **Entitlements + pricing-and-packaging** — Stigg.
- **Feature flags + remote config** — OpenFeature (the spec/SDK layer), Flagsmith, Unleash.
- **Identity governance / access lifecycle** — Microsoft Entra ID Entitlement Management.

For the head-to-head authorization comparison see [../comparison-matrix.md](../comparison-matrix.md);
for the full survey taxonomy and the ReBAC/policy-engine families see
[../market-survey.md](../market-survey.md).

Where this repo already integrates a product (OpenFeature and Unleash), the entry is grounded in
the shipped source rather than vendor marketing. For the **surveyed** (non-integrated) products,
every capability, hosting, SDK, and licensing claim is drawn from the primary sources consolidated
in the [Sources](#sources) section at the end — verify against those before relying on a specific
claim, as vendor offerings change over time.

---

## OpenMeter

- **Overview / category.** *Usage metering / metered billing.* OpenMeter is an open-source
  platform for real-time usage metering and usage-based billing, aimed at AI, API, and
  developer-tool products that price on consumption (tokens, API calls, compute-seconds). It is a
  **telemetry-and-billing** system, not an access gate.
- **Core model.** Ingests high-volume **usage events** (CloudEvents-formatted), aggregates them
  into **meters**, and exposes balances, entitlements/credits, and billing hooks (e.g. Stripe).
  The primitives are *events → meters → aggregated usage → invoice/limit*, not *subject →
  permission*.
- **Relationship to authorization.** Indirect. OpenMeter can enforce **usage entitlements** and
  prepaid-credit limits ("has this tenant exhausted their quota?"), which resembles a quota gate,
  but it does not evaluate per-resource, per-action access. In this repo's terms it would feed the
  **quota** side of entitlements (`PlanQuota` / `UsageCounter`), never the PDP's `AccessDecision`.
- **API / SDKs incl. .NET.** REST API with an OpenAPI specification; official SDKs for
  **Node.js, Python, and Go**. There is **no official .NET SDK** — .NET integration goes through
  the REST/OpenAPI surface (a generated or hand-rolled `HttpClient`).
- **Hosting.** Open-source self-host (Docker Compose / Kubernetes) plus a managed OpenMeter Cloud.
- **Licensing & maturity.** Core is **Apache-2.0**. Actively developed; positioned around
  AI/usage-based monetization.
- **Repo status (be accurate).** This repository's **full** OpenMeter integration is **planned,
  not shipped** — it is scoped to a later clickstop
  ([`planned_cs27_full-openmeter-and-azure-deploy.md`](../../../project/clickstops/planned/planned_cs27_full-openmeter-and-azure-deploy.md)).
  The entitlements service today models usage/quotas with its own domain types
  (`UsageCounter`, `PlanQuota`, `QuotaDecision`); OpenMeter is a future backend for that surface,
  not a current dependency.
- **Strengths.** Purpose-built for high-throughput metering; open-source core; CloudEvents-native;
  clean separation of metering from application logic.
- **Weaknesses.** No first-class .NET SDK; solves metering/billing, so it needs a packaging or
  authorization layer alongside it; operational overhead if self-hosting the streaming pipeline.
- **When to use.** You need real-time, event-driven usage metering to drive consumption billing or
  usage limits — and you will pair it with a separate entitlements/packaging and authorization
  layer.

## Stigg

- **Overview / category.** *Entitlements + pricing-and-packaging.* Stigg is a managed
  monetization platform that lets SaaS teams define plans, features, seats, and quotas centrally
  and enforce **entitlements** in the product without hard-coding pricing logic.
- **Core model.** Manages **plans / features / add-ons / seats / quotas** and the mapping from a
  customer's subscription to the concrete entitlements they hold. It typically sits between a
  billing provider (Stripe et al.) and the application, translating "what plan is this customer
  on?" into "which features and limits does this customer get right now?" This is conceptually the
  same job as this repo's `Plan` / `PlanTier` / `FeatureCatalog` / `EntitlementCatalog`, but as a
  hosted service.
- **Relationship to authorization.** It **gates features and enforces quotas** by subscription
  state — a *commercial* gate ("is this feature in the customer's plan?"), distinct from a
  *security* gate ("is this caller allowed to touch this resource?"). It complements, rather than
  replaces, a PDP.
- **API / SDKs incl. .NET.** GraphQL/REST APIs with SDKs across common backend and frontend
  stacks; Stigg publishes SDKs and integration guides in its developer docs. .NET coverage should
  be confirmed against current docs before relying on it (SDK availability changes); the REST/
  GraphQL API is the portable fallback for .NET.
- **Hosting.** Fully managed **SaaS** — no self-host option.
- **Licensing & maturity.** Commercial/proprietary, subscription-priced (pricing is not published;
  sales-led). Established in the pricing-and-packaging niche.
- **Strengths.** Removes bespoke entitlement plumbing; central pricing/packaging with fast plan
  iteration; billing-provider integrations; good fit for product-led SaaS.
- **Weaknesses.** Proprietary and SaaS-only (vendor lock-in, data-residency considerations);
  another external dependency in the request path; not an authorization engine.
- **When to use.** You want to externalize pricing/packaging and entitlement enforcement to a
  managed service and iterate on plans without shipping code — and you accept a proprietary,
  cloud-hosted dependency.

## OpenFeature (CNCF)

- **Overview / category.** *Feature-flag SDK specification (vendor-neutral).* OpenFeature is a
  CNCF project that standardizes a **feature-flag API/SDK** so application code is written once
  against a common client and the backing flag provider is swapped by configuration. It is a
  **specification + SDK layer**, not a flag-management backend itself.
- **Core model.** A common `Client` with typed `getBooleanValue` / `getStringValue` / etc. calls,
  an **evaluation context** (attributes used for targeting), and a pluggable **provider** that
  resolves the flag. Providers exist for many commercial and open-source flag systems.
- **Relationship to authorization.** Feature flags decide **feature exposure / rollout**, not
  security. A flag that is on for a plan tier is a *product* decision; it must not be mistaken for
  an access-control decision. In this repo, flags fail **closed** to `false` on unknown keys.
- **Repo grounding.** This repo integrates OpenFeature as its feature-gate seam:
  - [`OpenFeatureGate.cs`](../../../src/AuthzEntitlements.Entitlements.Service/Features/OpenFeatureGate.cs)
    evaluates flags through the OpenFeature `FeatureClient`, packing the tenant's `PlanTier` into
    the evaluation context under the `planTier` key and defaulting unknown flags to `false`
    (fail-closed).
  - [`InMemoryFeatureProviderFactory.cs`](../../../src/AuthzEntitlements.Entitlements.Service/Features/InMemoryFeatureProviderFactory.cs)
    builds the OpenFeature **in-memory provider** from the single `FeatureCatalog` policy, so each
    feature key becomes a boolean flag whose per-context evaluator reads `planTier` and returns
    `on`/`off` straight from `FeatureCatalog` — the flag provider and the `/plan` summary can never
    disagree.
  - The provider is chosen at
    [`FeatureProviderFactory.cs`](../../../src/AuthzEntitlements.Entitlements.Service/Features/FeatureProviderFactory.cs)
    (`InMemory` default vs `Unleash`, bound from
    [`EntitlementsFeatureOptions.cs`](../../../src/AuthzEntitlements.Entitlements.Service/Features/EntitlementsFeatureOptions.cs)).
- **API / SDKs incl. .NET.** First-class **.NET SDK** (`OpenFeature` on NuGet) — the very package
  this repo depends on — alongside SDKs for Java, Go, Node.js, Python, and more.
- **Hosting.** Not applicable in the usual sense: OpenFeature is a client-side spec/SDK. Hosting is
  determined by the chosen *provider* (in-memory, Unleash, Flagsmith, a SaaS, etc.).
- **Licensing & maturity.** **Apache-2.0**, CNCF-governed. Broad, growing provider ecosystem; the
  de-facto vendor-neutral flag standard.
- **Strengths.** Provider-agnostic — swap flag backends without touching application code; typed,
  context-aware evaluation; strong .NET support; avoids vendor lock-in at the SDK layer.
- **Weaknesses.** It is only the abstraction — you still need a real provider/management plane for
  targeting rules, audit, and UI. The abstraction can hide provider-specific capabilities.
- **When to use.** Any time you want feature flags without binding your code to one vendor — as
  this repo does, defaulting to a deterministic in-memory provider and allowing Unleash behind the
  same seam.

## Flagsmith

- **Overview / category.** *Feature-flag + remote-config platform.* Flagsmith is an open-source
  feature-flag and remote-configuration service with a management UI, environments, and
  segment-based targeting.
- **Core model.** **Flags + remote-config values** organized by project/environment, with
  **segments** and **identity/trait**-based targeting rules, percentage rollouts, and change
  history. It is a full management backend (server + UI), not just an SDK.
- **Relationship to authorization.** Same as flags generally: it controls **feature exposure and
  rollout**, not security authorization. Segment targeting can look like access control but is not
  a substitute for a PDP.
- **API / SDKs incl. .NET.** REST API plus official server- and client-side SDKs, including an
  official **.NET SDK** (`flagsmith-dotnet-client`). It can also be used behind an OpenFeature
  provider.
- **Hosting.** **Open-source self-host** (Docker / Kubernetes / source) or Flagsmith's managed
  cloud.
- **Licensing & maturity.** Core is **BSD-3-Clause** (permissive); some enterprise features are
  offered commercially. Mature, widely adopted.
- **Strengths.** Full flag + remote-config platform with UI; genuinely self-hostable under a
  permissive license; first-class .NET SDK; OpenFeature-compatible.
- **Weaknesses.** Running the full stack (API, dashboard, datastore) is operational work; advanced
  governance/enterprise capabilities sit behind the commercial tier.
- **When to use.** You want a self-hostable, permissively licensed flag + remote-config platform
  with a management UI, and you value the option to consume it through OpenFeature.

## Unleash

- **Overview / category.** *Feature-flag platform.* Unleash is an open-source feature-flag/toggle
  management system (server + UI) with activation strategies, gradual rollouts, and environments.
- **Core model.** **Toggles** governed by **activation strategies** (gradual rollout, user/segment
  constraints, variants) across environments, managed from a central server and evaluated by
  language SDKs. Boolean toggles are the primary primitive.
- **Relationship to authorization.** Feature exposure and rollout control — not a security gate.
- **Repo grounding.** This repo integrates Unleash **as an OpenFeature provider**, behind the same
  gate seam as the in-memory provider:
  - [`UnleashFeatureProvider.cs`](../../../src/AuthzEntitlements.Entitlements.Service/Features/UnleashFeatureProvider.cs)
    is a minimal OpenFeature `FeatureProvider` backed by the Unleash .NET client (`IUnleash`).
    Because Unleash exposes **boolean toggles**, boolean resolution delegates to
    `IUnleash.IsEnabled(flagKey, defaultValue)`; every non-boolean value type falls back to its
    default.
  - It is **config-gated**: constructed only when `Entitlements:FeatureProvider` is `Unleash`
    (requires a running Unleash server) and therefore never on the default/tested code path — the
    default is the deterministic in-memory provider (see `FeatureProviderFactory.cs`,
    `EntitlementsFeatureOptions.cs`). `FeatureProviderFactory` fails closed with a clear
    `InvalidOperationException` if `Unleash` is selected but the URL is unconfigured.
- **API / SDKs incl. .NET.** REST API plus official SDKs including a **.NET client**
  (`unleash-client-dotnet` / `Unleash.Client` on NuGet), which this repo consumes; usable directly
  or behind OpenFeature (as here).
- **Hosting.** **Open-source self-host** (server is Node.js) plus a managed/enterprise cloud.
- **Licensing & maturity.** Open-source core under **Apache-2.0**; an enterprise edition adds SSO,
  advanced access control, audit, and support. Mature and widely adopted.
- **Strengths.** Robust activation-strategy model; permissive OSS core; self-hostable; official
  .NET client; clean OpenFeature integration (as demonstrated in this repo).
- **Weaknesses.** Toggles are boolean-centric (non-boolean config is thinner); enterprise-grade
  governance is a paid tier; self-hosting adds operational surface.
- **When to use.** You want a mature, self-hostable, permissively licensed flag platform with rich
  rollout strategies — and, as this repo shows, you can adopt it without lock-in by consuming it
  through OpenFeature.

## Microsoft Entra ID — Entitlement Management

- **Overview / category.** *Identity governance / access lifecycle.* Entra ID Entitlement
  Management (part of Microsoft Entra ID Governance, formerly Azure AD Premium P2) automates the
  **request → approval → assignment → review → expiry** lifecycle for access to organizational
  resources. This is IGA (Identity Governance & Administration), not application feature-flagging
  or metering.
- **Core model.** **Access packages** bundle resources (groups, apps, SharePoint sites) with
  policies defining *who may request*, *approval workflow*, *duration/expiry*, and *access
  reviews*. **Access reviews** periodically re-certify that assignments are still warranted
  (least-privilege). The unit is the *access package assignment*, governed over time.
- **Relationship to authorization.** It governs **which coarse-grained access a person holds**
  (group/app/role membership) and the *lifecycle* of that access — an outer, administrative gate.
  It is complementary to a runtime PDP: Entitlement Management decides *whether Alice should have a
  role at all* (and for how long); a PDP decides *whether Alice's request right now satisfies the
  fine-grained rules*. In this repo's layering it maps to the coarse edge, not the fine-grained
  PDP.
- **API / SDKs incl. .NET.** Microsoft Graph API (entitlement-management resources) with the
  Microsoft Graph **.NET SDK**; full portal and PowerShell tooling.
- **Hosting.** Managed **SaaS** as part of Microsoft Entra / Azure — no self-host.
- **Licensing & maturity.** Requires **Microsoft Entra ID Governance** / **Entra ID P2**
  licensing for admins and governed users. Enterprise-grade and mature within the Microsoft
  identity stack.
- **Strengths.** Turnkey governance (approvals, time-bound access, recurring reviews, audit);
  deep Entra/Microsoft 365 integration; strong compliance/attestation story.
- **Weaknesses.** Governs coarse organizational access, not per-request/per-resource application
  authorization; Microsoft-ecosystem- and P2-license-bound; SaaS-only.
- **When to use.** You are in the Microsoft identity ecosystem and need to govern the *lifecycle*
  of access (joiner/mover/leaver, external collaboration, periodic recertification) — layered
  above, not instead of, a fine-grained PDP.

---

## Synthesis

These six products solve **four different problems**, and treating them as interchangeable is the
core risk this survey guards against:

| Category | Product(s) | Answers | Relationship to the PDP |
|---|---|---|---|
| Usage metering / billing | OpenMeter | "How much has this tenant used?" | Feeds quotas, not access decisions |
| Entitlements / packaging | Stigg | "What does this plan include?" | Commercial gate, alongside the PDP |
| Feature flags / config | OpenFeature, Flagsmith, Unleash | "Is this feature on for this tier?" | Exposure/rollout, not security |
| Identity governance | Entra Entitlement Management | "Should this person hold this access, still?" | Coarse lifecycle gate, above the PDP |

Key takeaways:

- **Billing/packaging/flag state is not an authorization decision.** Each must fail *closed* and
  stay distinct from the PDP's `AccessDecision`. This repo enforces that: flags default to `false`,
  entitlement/quota checks deny on the local catalog, and the PDP is a separate seam.
- **OpenFeature is the right abstraction layer** for flags — this repo writes against it once and
  swaps between the deterministic in-memory provider and Unleash by configuration, avoiding
  vendor lock-in.
- **Entitlements (`Plan`/`FeatureCatalog`) and metering (`UsageCounter`/`PlanQuota`) already live
  in-repo**; OpenMeter (CS27) and platforms like Stigg are *future/alternative* backends for that
  same surface, not current dependencies.

See the authorization-engine comparison in
[../comparison-matrix.md](../comparison-matrix.md), the broader taxonomy in
[../market-survey.md](../market-survey.md), and the AuthZEN standard alignment in
[authzen.md](authzen.md).

## Sources

- OpenMeter — GitHub (README, Apache-2.0 LICENSE): <https://github.com/openmeterio/openmeter>;
  docs: <https://openmeter.io/docs>
- Stigg — product and developer docs: <https://www.stigg.io/> · <https://docs.stigg.io/>
- OpenFeature (CNCF) — spec and .NET SDK: <https://openfeature.dev/> ·
  <https://github.com/open-feature/dotnet-sdk>
- Flagsmith — GitHub (BSD-3-Clause), docs, .NET client:
  <https://github.com/Flagsmith/flagsmith> · <https://docs.flagsmith.com/> ·
  <https://github.com/Flagsmith/flagsmith-dotnet-client>
- Unleash — GitHub (Apache-2.0), .NET client: <https://github.com/Unleash/unleash> ·
  <https://github.com/Unleash/unleash-client-dotnet> · <https://www.getunleash.io/>
- Microsoft Entra ID Governance / Entitlement Management — Microsoft Learn:
  <https://learn.microsoft.com/entra/id-governance/entitlement-management-overview> ·
  licensing: <https://learn.microsoft.com/entra/id-governance/entitlement-management-licensed-features>

> Vendor capabilities, SDK coverage, and licensing change over time; verify against the primary
> sources above before relying on a specific claim. Repo-integration statements (OpenFeature,
> Unleash) are grounded in the source files cited inline and are authoritative for this repository.
