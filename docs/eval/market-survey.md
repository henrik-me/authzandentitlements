# Market survey

This is the landing page for the project's broad scan of the **authorization** and
**entitlements** market. It groups the engines and products we examined into families,
points at the per-family deep-dives, and gives a single index of every surveyed engine so
you can jump straight to its write-up.

It is an **index**, not a duplicate: the strengths / weaknesses / when-to-use detail lives
in the linked survey sub-documents, the quantitative and dimensional comparison lives in the
[comparison matrix](comparison-matrix.md), and the decisions we actually took are recorded in
the [ADR index](../adr/README.md).

## Scope and methodology

The survey deliberately spans more of the market than this repository integrates, so the
matrix and ADRs are grounded in a real landscape rather than a shortlist. Two tiers of
evidence back every claim — the same framing the [comparison matrix](comparison-matrix.md)
uses:

- **Integrated in-repo** (real evidence). Engines wired behind the unified PDP seam and held
  to the shared scenario catalog: `reference`, `aspnet`, `casbin`, `cedar`, `opa`, and
  `openfga` for authorization; `InMemory` and `Unleash` (OpenFeature) for entitlements /
  flags. Claims about these cite shipped code and docs.
- **Surveyed** (secondary research). Everything else below is summarized from vendor docs,
  papers, and public sources — not measured in this repo. A `†` marks a surveyed engine that
  is **also** integrated here.

## Taxonomy

The market sorts into three engine families plus the cross-cutting decision standard.

- **Relationship-based / Zanzibar** — see
  [ReBAC / Zanzibar survey](survey/relationship-based-zanzibar.md). Model authorization as a
  graph of relationship tuples with a reverse-index (`list-objects`): OpenFGA `†`, SpiceDB,
  Ory Keto, Topaz / Aserto, Permify, and Warrant / WorkOS FGA.
- **Policy-as-code / decision engines** — see
  [Policy & decision engines survey](survey/policy-and-decision-engines.md). Evaluate a
  request against externalized policy, typically per-request and stateless: OPA / Rego `†`,
  Cedar (+ AVP) `†`, Casbin `†`, Oso (+ Oso Cloud), Cerbos (+ Cerbos Hub), and Permit.io.
- **Entitlements / metering / feature-flags** — see
  [Entitlements & flags survey](survey/entitlements-and-flags.md). Govern what a plan or
  subscription is allowed to use, meter usage, and gate features: OpenMeter, Stigg,
  OpenFeature (CNCF), Flagsmith, Unleash `†`, and Microsoft Entra ID Entitlement Management.
- **Standards** — see [AuthZEN survey](survey/authzen.md). The OpenID AuthZEN decision-API
  standard that unifies the `subject / action / resource / context` → `permit/deny` shape
  used throughout this repo.

## Index

Every surveyed engine / product, its family, and a direct link to its section.

| Engine / product | Family | Section |
|---|---|---|
| OpenFGA `†` | Relationship-based / Zanzibar | [relationship-based-zanzibar.md#openfga](survey/relationship-based-zanzibar.md#openfga) |
| SpiceDB | Relationship-based / Zanzibar | [relationship-based-zanzibar.md#spicedb](survey/relationship-based-zanzibar.md#spicedb) |
| Ory Keto | Relationship-based / Zanzibar | [relationship-based-zanzibar.md#ory-keto](survey/relationship-based-zanzibar.md#ory-keto) |
| Topaz / Aserto | Relationship-based / Zanzibar | [relationship-based-zanzibar.md#topaz--aserto](survey/relationship-based-zanzibar.md#topaz--aserto) |
| Permify | Relationship-based / Zanzibar | [relationship-based-zanzibar.md#permify](survey/relationship-based-zanzibar.md#permify) |
| Warrant / WorkOS FGA | Relationship-based / Zanzibar | [relationship-based-zanzibar.md#warrant-now-workos-fga](survey/relationship-based-zanzibar.md#warrant-now-workos-fga) |
| OPA / Rego `†` | Policy-as-code / decision engines | [policy-and-decision-engines.md#open-policy-agent-opa--rego](survey/policy-and-decision-engines.md#open-policy-agent-opa--rego) |
| Cedar (+ AVP) `†` | Policy-as-code / decision engines | [policy-and-decision-engines.md#cedar--amazon-verified-permissions](survey/policy-and-decision-engines.md#cedar--amazon-verified-permissions) |
| Casbin `†` | Policy-as-code / decision engines | [policy-and-decision-engines.md#casbin](survey/policy-and-decision-engines.md#casbin) |
| Oso (+ Oso Cloud) | Policy-as-code / decision engines | [policy-and-decision-engines.md#oso-polar-and-oso-cloud](survey/policy-and-decision-engines.md#oso-polar-and-oso-cloud) |
| Cerbos (+ Cerbos Hub) | Policy-as-code / decision engines | [policy-and-decision-engines.md#cerbos--cerbos-hub](survey/policy-and-decision-engines.md#cerbos--cerbos-hub) |
| Permit.io | Policy-as-code / decision engines | [policy-and-decision-engines.md#permitio](survey/policy-and-decision-engines.md#permitio) |
| OpenMeter | Entitlements / metering / flags | [entitlements-and-flags.md#openmeter](survey/entitlements-and-flags.md#openmeter) |
| Stigg | Entitlements / metering / flags | [entitlements-and-flags.md#stigg](survey/entitlements-and-flags.md#stigg) |
| OpenFeature (CNCF) | Entitlements / metering / flags | [entitlements-and-flags.md#openfeature-cncf](survey/entitlements-and-flags.md#openfeature-cncf) |
| Flagsmith | Entitlements / metering / flags | [entitlements-and-flags.md#flagsmith](survey/entitlements-and-flags.md#flagsmith) |
| Unleash `†` | Entitlements / metering / flags | [entitlements-and-flags.md#unleash](survey/entitlements-and-flags.md#unleash) |
| Entra ID Entitlement Management | Entitlements / metering / flags | [entitlements-and-flags.md#microsoft-entra-id--entitlement-management](survey/entitlements-and-flags.md#microsoft-entra-id--entitlement-management) |
| OpenID AuthZEN | Standards | [authzen.md](survey/authzen.md) |

## See also

- [Comparison matrix](comparison-matrix.md) — the at-a-glance, dimension-by-engine comparison.
- [ReBAC / Zanzibar survey](survey/relationship-based-zanzibar.md) — the Zanzibar family.
- [Policy & decision engines survey](survey/policy-and-decision-engines.md) — policy-as-code.
- [Entitlements & flags survey](survey/entitlements-and-flags.md) — metering and feature-flags.
- [AuthZEN survey](survey/authzen.md) — the OpenID decision-API standard.
- [ADR index](../adr/README.md) — the decisions taken, with when-to-use guidance per engine.
