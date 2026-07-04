# Compliance control mapping — SOX / PCI-DSS / GDPR

> **Scope:** map the concrete, **already-shipped** authorization, governance, and
> audit controls in this lab to the requirements of three regulatory frameworks —
> **SOX** (ITGC / §404), **PCI-DSS v4.0**, and the **GDPR** — with a verifiable
> `file:line` citation for every control and a pointer to the exact **evidence
> surface** (endpoint, report command, or dashboard) that produces evidence for
> it. It is the CS22 deliverable and builds on the shipped-control style of the
> [STRIDE threat model](../security/threat-model.md).

## Status & how to read this document

- **System class:** a **local-first developer / evaluation lab** and reusable
  reference architecture — **not** a certified production system. It runs under
  the .NET Aspire AppHost on a single developer machine.
- **This mapping is illustrative and educational.** It shows *how* the lab's
  four-layer authorization model plus governance and a tamper-evident audit log
  line up with common control objectives. It is **not** an attestation, a
  certification, nor an assertion of compliance. A real deployment would still
  need the organizational, physical, and operational controls these frameworks
  require (the **Residual / notes** column names the biggest gaps for each row).
- **No control is claimed without code behind it.** Every row cites an openable
  `file:line`. Where a framework expects a control the lab does **not** ship
  (e.g. per-producer authenticated audit ingest, edge rate limiting), that is
  recorded honestly as a residual, not overstated. See
  [§ Known gaps and residuals](#known-gaps-and-residuals).
- **How to read a row:** *framework requirement* → *shipped control (the
  mechanism)* → *`file:line` citation you can open and verify* → *evidence
  surface (the endpoint / `dotnet run` report / dashboard that produces
  evidence)* → *residual / notes (what a production adopter still owes)*.

## The control surface at a glance

The lab exercises four complementary authorization layers over a fintech
back-office domain, wrapped by access governance and a tamper-evident audit log.
These are the reusable compliance building blocks the tables below map:

| Layer | Mechanism | Primary source |
|---|---|---|
| **AuthN** | OIDC/JWT bearer validation at the edge **and** the API (issuer, audience, signature, lifetime; tightened clock-skew; literal claims) | [`AuthenticationSetup.cs`](../../src/AuthzEntitlements.Bank.Api/Auth/AuthenticationSetup.cs), [`GatewayAuthenticationSetup.cs`](../../src/AuthzEntitlements.Edge.Gateway/Auth/GatewayAuthenticationSetup.cs) |
| **Coarse authZ** | Edge policies: authenticated + tenant-present + required scope, audited at the edge | [`CoarseAuthorization.cs`](../../src/AuthzEntitlements.Edge.Gateway/Auth/CoarseAuthorization.cs), [`GatewayAuditMiddleware.cs`](../../src/AuthzEntitlements.Edge.Gateway/Audit/GatewayAuditMiddleware.cs) |
| **Fine authZ (PDP)** | Default-deny decision provider with explainable, audited verdicts | [`ReferenceDecisionProvider.cs`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs), [`PdpDecisionService.cs`](../../src/AuthzEntitlements.Authz.Pdp/Services/PdpDecisionService.cs) |
| **SoD / maker-checker** | Toxic role-pair policy + maker≠checker invariant + a $10,000 approval threshold | [`GovernanceSodPolicy.cs`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Sod/GovernanceSodPolicy.cs), [`Approval.cs`](../../src/AuthzEntitlements.Bank.Api/Domain/Approval.cs) |
| **Access governance** | JIT time-bound grants, maker-checker+SoD approval, recertification campaigns, least-privilege queries | [`GovernanceEndpoints.cs`](../../src/AuthzEntitlements.Governance.Service/Endpoints/GovernanceEndpoints.cs), [access-governance.md](../governance/access-governance.md) |
| **Audit** | Append-only, hash-chained log with a `/verify` recompute and a filtered read model | [`AuditHashChain.cs`](../../src/AuthzEntitlements.Audit.Service/Domain/AuditHashChain.cs), [`AuditEndpoints.cs`](../../src/AuthzEntitlements.Audit.Service/Endpoints/AuditEndpoints.cs), [audit-pipeline.md](../authz/audit-pipeline.md) |
| **Observability** | Metrics/traces/logs persisted in Grafana; per-domain dashboards | [observability-stack.md](../observability/observability-stack.md), [`infra/observability/grafana/dashboards/`](../../infra/observability/grafana/dashboards/) |

Line numbers in the citation columns below were verified against the code at the
time of writing (CS22). If a refactor shifts them, the anchor names in the source
remain the source of truth.

## SOX — ITGC / §404 (IT general controls over financial reporting)

SOX §404 turns on **IT general controls**: logical access, segregation of
duties, change/access certification, and a reliable audit trail. The lab ships a
concrete mechanism for each.

| SOX control objective | Shipped control (mechanism) | Citation (`file:line`) | Evidence surface | Residual / notes |
|---|---|---|---|---|
| **Logical access — authenticate** | OIDC/JWT bearer validated at both gates: issuer, audience, signature (JWKS), lifetime, tightened 30s clock skew, expiration & signed-token required | [`AuthenticationSetup.cs:117-134`](../../src/AuthzEntitlements.Bank.Api/Auth/AuthenticationSetup.cs#L117-L134), [`GatewayAuthenticationSetup.cs:118-135`](../../src/AuthzEntitlements.Edge.Gateway/Auth/GatewayAuthenticationSetup.cs#L118-L135) | Edge audit events (`GatewayAuditMiddleware`); PDP audit trail via `GET /api/audit/entries` | Trust rests on Keycloak signing keys; dev uses plain-HTTP metadata ([`:115`](../../src/AuthzEntitlements.Bank.Api/Auth/AuthenticationSetup.cs#L115)). Prod: HTTPS-only, key rotation |
| **Logical access — authorize (RBAC + scopes)** | Coarse edge policies require authenticated + tenant + scope; the fine PDP re-authorizes with role/scope/tenant rules and **defaults to deny** for any unknown action | [`CoarseAuthorization.cs:28-45`](../../src/AuthzEntitlements.Edge.Gateway/Auth/CoarseAuthorization.cs#L28-L45), [`ReferenceDecisionProvider.cs:40-52`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs#L40-L52) | PDP decisions via `GET /api/audit/entries?action=...`; `AuthzEntitlements.Compliance` reports | Branch-level ABAC deferred; role→user assignment lives in the IdP/directory (adopter) |
| **Segregation of duties (SoD)** | Maker≠checker enforced in the domain aggregate; toxic role-pair policy (5 incompatible pairs) blocks incompatible role sets; a $10,000 threshold obliges a second-person approval | [`Approval.cs:32-35`](../../src/AuthzEntitlements.Bank.Api/Domain/Approval.cs#L32-L35), [`GovernanceSodPolicy.cs:27-34`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Sod/GovernanceSodPolicy.cs#L27-L34), [`ReferenceDecisionProvider.cs:15,154-159`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs#L154-L159) | `AuthzEntitlements.Compliance` **SoD report**; governance approvals via `GET /api/audit/entries`; Grafana `compliance` dashboard (SoD denials) | SoD role-pair set is illustrative; a real org tailors the toxic-combination catalog |
| **Access certification / recertification** | Access-review campaigns enumerate active grants and record a per-item Certify/Revoke decision; a Revoke revokes the linked grant; the campaign completes when no item is pending | [`GovernanceEndpoints.cs:474-539`](../../src/AuthzEntitlements.Governance.Service/Endpoints/GovernanceEndpoints.cs#L474-L539), [`:541-634`](../../src/AuthzEntitlements.Governance.Service/Endpoints/GovernanceEndpoints.cs#L541-L634) | `POST /api/governance/review-campaigns/{id}/run`, `POST /api/governance/review-items/{id}/decision`, `GET /api/governance/review-campaigns`; `AuthzEntitlements.Compliance` **access-certification report** | No scheduler/reminders/escalation workflow (adopter) |
| **Change/access approval (maker-checker on elevation)** | JIT access requests are approved through a maker-checker + SoD gate: the requester cannot approve their own elevation; the approver must be a known checker-eligible principal; PDP outage fails closed | [`GovernanceEndpoints.cs:150-213`](../../src/AuthzEntitlements.Governance.Service/Endpoints/GovernanceEndpoints.cs#L150-L213) | `POST /api/governance/requests/{id}/approve`; governance decisions via `GET /api/audit/entries` | Approver eligibility is role-derived; delegation/segmentation is adopter policy |
| **Audit trail integrity** | Append-only hash chain: each row binds its sequence, the prior row's hash, and every content field via SHA-256; `/verify` recomputes and reports the first break; an optional trusted checkpoint catches tail truncation & full-suffix rewrite | [`AuditHashChain.cs:106-135`](../../src/AuthzEntitlements.Audit.Service/Domain/AuditHashChain.cs#L106-L135), [`:155-172`](../../src/AuthzEntitlements.Audit.Service/Domain/AuditHashChain.cs#L155-L172) | `GET /api/audit/verify` (with optional `expectedSequence`/`expectedRowHash`); `AuthzEntitlements.Compliance` **audit-integrity report** | Tamper **detection**, not prevention; anonymous intra-cluster ingest (dev). Prod: authenticated ingest + external anchoring |
| **Least privilege (JIT / time-bound access)** | Grants are time-bound; the active-grant query filters `RevokedAt == null && ExpiresAt > now`, so expired/revoked access is never returned; grants can be revoked on demand | [`GovernanceEndpoints.cs:332-352`](../../src/AuthzEntitlements.Governance.Service/Endpoints/GovernanceEndpoints.cs#L332-L352), [`:391-429`](../../src/AuthzEntitlements.Governance.Service/Endpoints/GovernanceEndpoints.cs#L391-L429) | `GET /api/governance/principals/{id}/grants`, `POST /api/governance/grants/{id}/revoke`; `AuthzEntitlements.Compliance` **least-privilege attestation** | Baseline-role provisioning is directory-driven (adopter) |

## PCI-DSS v4.0

The lab maps cleanly to the three PCI-DSS requirement families about access and
logging — **Req 7** (least privilege / need-to-know), **Req 8** (identify &
authenticate), and **Req 10** (log & monitor). The payment-card data model itself
is out of scope (this is an authorization lab, not a cardholder-data
environment); the mapping is to the *access-control mechanisms* PCI requires.

| PCI-DSS requirement | Shipped control (mechanism) | Citation (`file:line`) | Evidence surface | Residual / notes |
|---|---|---|---|---|
| **Req 7.2 — restrict access by role / need-to-know** | RBAC + scopes at the edge and PDP; default-deny for unknown actions; tenant isolation (subject tenant must match resource tenant, fail-closed on a blank tenant) | [`ReferenceDecisionProvider.cs:40-52`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs#L40-L52), [`:211-214`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs#L211-L214), [`CoarseAuthorization.cs:28-45`](../../src/AuthzEntitlements.Edge.Gateway/Auth/CoarseAuthorization.cs#L28-L45) | PDP decisions via `GET /api/audit/entries?decision=Deny`; Grafana `compliance` dashboard (entitlements/authz denials) | Data-classification-driven access rules are adopter policy |
| **Req 7.2.5 — least privilege for access, time-limited** | JIT time-bound grants with read-time expiry enforcement and on-demand revoke; recertification campaigns prune standing access | [`GovernanceEndpoints.cs:332-352`](../../src/AuthzEntitlements.Governance.Service/Endpoints/GovernanceEndpoints.cs#L332-L352), [`:391-429`](../../src/AuthzEntitlements.Governance.Service/Endpoints/GovernanceEndpoints.cs#L391-L429) | `GET /api/governance/principals/{id}/grants`, `POST /api/governance/grants/{id}/revoke`; `AuthzEntitlements.Compliance` least-privilege attestation | — |
| **Req 8.2 / 8.3 — identify & authenticate users** | OIDC/JWT bearer with issuer/audience/signature/lifetime validation at both gates; literal claim mapping (`MapInboundClaims=false`) so roles/scope are read verbatim | [`AuthenticationSetup.cs:104,117-134`](../../src/AuthzEntitlements.Bank.Api/Auth/AuthenticationSetup.cs#L117-L134), [`GatewayAuthenticationSetup.cs:105,118-135`](../../src/AuthzEntitlements.Edge.Gateway/Auth/GatewayAuthenticationSetup.cs#L118-L135) | Edge audit events; `GET /api/audit/entries` (subject on every decision) | MFA, credential lifecycle, and session policy are the IdP's job (Keycloak/Entra ID) |
| **Req 8.5 — no shared/generic auth for security functions** | Maker is bound to the token subject; a caller may not act as another user (create) or decide as another user (approve) | [`TransactionEndpoints.cs:57-62`](../../src/AuthzEntitlements.Bank.Api/Endpoints/TransactionEndpoints.cs#L57-L62), [`:158-163`](../../src/AuthzEntitlements.Bank.Api/Endpoints/TransactionEndpoints.cs#L158-L163) | `GET /api/audit/entries` (subject vs. maker/checker) | Anonymous intra-cluster service calls (dev); prod: per-service identity |
| **Req 10.2 — log all access to system components** | Every PDP decision emits exactly one audit event through a non-blocking sink, carrying subject, action, resource, decision, reason, tenant, and the determining rule | [`PdpDecisionService.cs:72-88`](../../src/AuthzEntitlements.Authz.Pdp/Services/PdpDecisionService.cs#L72-L88) | `GET /api/audit/entries` (filter by subject/action/decision/tenant/trace) | Events can be dropped under channel backpressure (availability-over-completeness by design) |
| **Req 10.3 — protect logs from alteration** | Hash-chained append-only log; `/verify` proves the chain is intact and reports the first break; producer stamped server-side (`"pdp"`), not caller-supplied | [`AuditHashChain.cs:106-135`](../../src/AuthzEntitlements.Audit.Service/Domain/AuditHashChain.cs#L106-L135), [`AuditEndpoints.cs:34-48`](../../src/AuthzEntitlements.Audit.Service/Endpoints/AuditEndpoints.cs#L34-L48) | `GET /api/audit/verify`; `AuthzEntitlements.Compliance` audit-integrity report | Detection not prevention; no WORM/off-box archival (adopter) |
| **Req 10.4 / 10.6 — review logs & retain telemetry** | Metrics, traces, and logs persist in a Grafana (LGTM) stack that survives restarts; per-domain dashboards visualize decision/denial rates | [observability-stack.md](../observability/observability-stack.md), [`pdp-performance.json`](../../infra/observability/grafana/dashboards/pdp-performance.json) | Grafana `compliance`, `service-health`, `request-rates`, `pdp-performance` dashboards | Grafana is an anonymous kiosk (dev); no alerting/retention policy (adopter) |

## GDPR

The GDPR mapping is to the **security & accountability** obligations an
authorization/audit system supports — not to the lawful-basis, consent, or
data-subject-rights machinery, which are application- and process-level concerns
outside this lab.

| GDPR article | Shipped control (mechanism) | Citation (`file:line`) | Evidence surface | Residual / notes |
|---|---|---|---|---|
| **Art 5(2) — accountability** | Tamper-evident, hash-chained audit trail that can prove *which* authorization decisions were made and that the record was not altered | [`AuditHashChain.cs:155-172`](../../src/AuthzEntitlements.Audit.Service/Domain/AuditHashChain.cs#L155-L172) | `GET /api/audit/verify`; `GET /api/audit/entries`; `AuthzEntitlements.Compliance` audit-integrity report | Accountability for *processing* (not just authz) needs app-level logging (adopter) |
| **Art 25 — data protection by design & by default** | Fail-closed authorization everywhere: unknown action → deny; missing/blank tenant → deny; PDP/engine outage → deny, never silent allow; least-privilege JIT grants | [`ReferenceDecisionProvider.cs:48-52`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs#L48-L52), [`:211-214`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs#L211-L214), [`GovernanceEndpoints.cs:215-232`](../../src/AuthzEntitlements.Governance.Service/Endpoints/GovernanceEndpoints.cs#L215-L232) | PDP decisions via `GET /api/audit/entries`; least-privilege attestation report | Data-minimization/pseudonymization of the domain data itself is out of scope |
| **Art 30 — records of processing activities** | Filtered, paged audit read model over every recorded decision (by subject, action, decision, tenant, trace, producer) | [`AuditEndpoints.cs:89-158`](../../src/AuthzEntitlements.Audit.Service/Endpoints/AuditEndpoints.cs#L89-L158) | `GET /api/audit/entries?subject=...&action=...&tenant=...` | This is a decision log, not a full ROPA; ROPA also needs purpose/retention metadata (adopter) |
| **Art 32(1)(b) — confidentiality & integrity** | AuthN + layered authZ (confidentiality) and the hash-chained log (integrity); every decision is explainable and audited | [`PdpDecisionService.cs:42-44,72-88`](../../src/AuthzEntitlements.Authz.Pdp/Services/PdpDecisionService.cs#L72-L88), [`DecisionExplanations.cs:13-26`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/DecisionExplanations.cs#L13-L26) | `GET /api/audit/verify`; `GET /api/audit/entries` (determining rule per decision) | No at-rest/in-transit encryption between services (dev); prod: mTLS + disk encryption |
| **Art 32(1)(d) — regularly test & evaluate access** | Access-review campaigns re-certify standing access; SoD policy blocks toxic role combinations; grants are time-bound and revocable | [`GovernanceEndpoints.cs:474-539`](../../src/AuthzEntitlements.Governance.Service/Endpoints/GovernanceEndpoints.cs#L474-L539), [`GovernanceSodPolicy.cs:39-55`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Sod/GovernanceSodPolicy.cs#L39-L55) | `POST /api/governance/review-campaigns/{id}/run`; `AuthzEntitlements.Compliance` SoD + access-certification reports | Cadence/scheduling of reviews is an operational policy (adopter) |

## Producing the evidence

Every mapped control points at a **reproducible** evidence artifact. Bring the
stack up with the Aspire AppHost, then:

### Live endpoints (via the Aspire-hosted services)

| Evidence | Command | What it demonstrates |
|---|---|---|
| Audit-chain integrity | `GET /api/audit/verify` (optionally `?expectedSequence=N&expectedRowHash=<hex>`) | The append-only log is intact; the checkpoint form catches truncation/rewrite |
| Decision / access records | `GET /api/audit/entries?subject=&action=&decision=&tenant=&trace=` | The Art 30 / Req 10 record of authorization decisions |
| Access certification | `GET /api/governance/review-campaigns`, `POST /api/governance/review-campaigns/{id}/run`, `POST /api/governance/review-items/{id}/decision` | Recertification campaigns and per-item Certify/Revoke outcomes |
| Least-privilege snapshot | `GET /api/governance/principals/{id}/grants` (active grants only), `POST /api/governance/grants/{id}/revoke` | Time-bound, revocable grants — no standing over-provisioning |
| Maker-checker approval | `POST /api/governance/requests/{id}/approve` | SoD-gated elevation approval (requester cannot self-approve) |

### The compliance evidence report tool

A dedicated console generates the offline evidence artifacts (JSON + Markdown):

```
dotnet run --project src/AuthzEntitlements.Compliance
```

It produces an **SoD report**, an **audit-integrity report**, an
**access-certification report**, and a **least-privilege attestation report**.
Pass `--governance-url <url>` to probe the live Governance service instead of the
built-in reference data.

### Grafana dashboards

Provisioned into the persistent Grafana (LGTM) stack (see
[observability-stack.md](../observability/observability-stack.md)):

- **`compliance`** — SoD denials, governance decisions/grants/reviews, and
  entitlements denials (the CS22 compliance dashboard).
- **`pdp-performance`**, **`service-health`**, **`request-rates`** — the CS12/CS20
  baseline dashboards for decision latency, service health, and request rates.

## Known gaps and residuals

Recorded honestly so an adopter can see the boundary between what the lab ships
and what a certified deployment still owes. These are **not** claimed as
controls:

- **Audit ingest is anonymous intra-cluster (dev).** Any in-cluster caller can
  `POST /api/audit/decisions`; producer identity is stamped server-side but not
  authenticated ([`AuditEndpoints.cs:34-48`](../../src/AuthzEntitlements.Audit.Service/Endpoints/AuditEndpoints.cs#L34-L48)). Prod: per-producer service identity (mTLS / tokens).
- **Tamper-evidence is detection, not prevention.** The hash chain detects
  alteration; it does not stop a fully-privileged actor from rewriting history
  without an **external anchor** of the tail hash (documented in
  [audit-pipeline.md](../authz/audit-pipeline.md)).
- **No edge rate limiting / throttling** — a token flood is not blunted at the
  gateway today (STRIDE DoS residual).
- **Grafana is an anonymous kiosk (dev)** with login disabled — a production
  adopter must put dashboards behind authentication with scoped datasources.
- **Transport is not mutually authenticated/encrypted between services (dev).**
  Prod needs mTLS and at-rest encryption for the Req 32 confidentiality claim.
- **The SoD role-pair catalog and the $10,000 threshold are illustrative** — a
  real organization tailors both to its own toxic-combination and materiality
  policies.
- **Framework coverage is partial by design.** This maps the *technical access,
  governance, and audit* controls only; organizational, physical, lawful-basis,
  consent, breach-notification, and data-subject-rights obligations are out of
  scope for a reference authorization lab.

## References

- [threat-model.md](../security/threat-model.md) — the STRIDE threat model whose
  shipped-control-at-`file:line` style this document follows.
- [secrets-and-least-privilege.md](../security/secrets-and-least-privilege.md) —
  the secrets inventory and least-privilege review (a key GDPR/PCI evidence base).
- [access-governance.md](../governance/access-governance.md) — access packages,
  JIT grants, maker-checker + SoD approval, and review campaigns.
- [audit-pipeline.md](../authz/audit-pipeline.md) — the hash-chained,
  tamper-evident audit log and its verify semantics.
- [observability-stack.md](../observability/observability-stack.md) — the
  persistent Grafana (LGTM) stack and dashboard provisioning.
- [CS22 plan](../../project/clickstops/active/active_cs22_compliance-mapping.md) —
  goal, deliverables, and exit criteria.
