# CS18 — Security hardening + threat model

**Status:** done
**Owner:** yoga-ae
**Branch:** cs18/content
**Started:** 2026-07-04
**Closed:** 2026-07-04
**Phase:** Cross-cutting
**Lane:** Cross-cutting
**Depends on:** CS04, CS05

## Goal

Threat-model and harden the authorization system itself (high-risk).

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 684f9105f415 | 2026-07-02T19:47:54Z | Go-with-amendments | CS04 and CS05 cover gateway/PDP fail-closed; track engine-specific tuple/policy tamper controls as follow-on work. |

## Deliverables

- STRIDE threat-model doc.
- Mitigations: tuple/policy tampering, confused-deputy, token replay/forgery.
- Fail-closed defaults on PDP outage; secrets management; least-privilege review.

## Exit criteria

- TM doc reviewed; fail-closed verified by tests; hardening items closed or tracked.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Author STRIDE TM (docs/security/threat-model.md) | done | yoga-ae | agent-id=yoga-ae/threat-model \| role=implementer \| report-status=complete \| learnings=0; 265-line STRIDE doc, all controls cited+verified |
| Secrets + least-privilege review (docs/security/secrets-and-least-privilege.md) | done | yoga-ae | agent-id=yoga-ae/secrets-lp \| role=implementer \| report-status=complete \| learnings=1; 142-line doc; caught recon error (Bank.Api is internal-only, not external) |
| Verify fail-closed + token-skew/expiration hardening | done | yoga-ae | agent-id=yoga-ae/token-hardening \| role=implementer \| report-status=complete \| learnings=1; existing fail-closed paths verified (CS04–CS11); added MaxClockSkew=30s + RequireExpirationTime + RequireSignedTokens to both JWT setups |
| Add security tests (token forgery/replay/expiry/skew) | done | yoga-ae | agent-id=yoga-ae/token-hardening \| role=implementer \| report-status=complete \| learnings=0; 2 new xUnit files (config + functional reject tests); Bank.Api 61/61, Gateway 58/58 |
| Track follow-on: engine tuple/policy tamper signing | pending | — | Per Plan review R1 amendment — file planned CS for OpenFGA tuple / OPA+Cedar policy integrity signing (HMAC/cosign); out of CS18 scope |
| Close-out: docs + restart state | pending | — | Update WORKBOARD.md, CONTEXT.md, and relevant docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md and create planned follow-up CSs for unresolved issues |

## Notes / Learnings

- **Posture was already strong.** Reconnaissance confirmed token validation (issuer/audience/signature/lifetime, `MapInboundClaims=false`, HTTPS-metadata-outside-Dev), confused-deputy binding (maker/checker/tenant bound to token or trusted resource row, never caller input), and fail-closed-on-outage across every PDP engine + entitlements + governance SoD were already implemented and tested (CS03–CS11). CS18 is therefore predominantly a threat-model + verification + targeted-hardening CS.
- **Hardening applied:** explicit tightened `ClockSkew = 30s` (from the 5-min .NET default), `RequireExpirationTime = true`, and `RequireSignedTokens = true` added to both JWT setups (Bank.Api + Edge.Gateway), reducing the token replay/expired-token window and rejecting unsigned/non-expiring tokens.
- **Deliverables:** `docs/security/threat-model.md` (STRIDE across 10 trust boundaries, each threat → cited control / residual risk / follow-on) and `docs/security/secrets-and-least-privilege.md` (dev-only secrets inventory + least-privilege review + prod hardening checklist). New `docs/security/` folder.
- **Recon correction (verified against source):** Bank.Api is NOT externally exposed (no `WithExternalHttpEndpoints`); only Grafana, edge-gateway, and bank-web are external — Bank.Api is already internal-only behind the gateway (the desired hardened posture).
- **Tracked follow-on (per R1 plan-review amendment):** engine-specific tuple/policy tamper controls (cryptographic signing / HMAC of OPA/Cedar policy bundles + OpenFGA tuples) are documented as residual risk + backlog, to be filed as a future CS — drift detection (CS17 golden snapshot SHA-256) is integrity *observation*, not *authentication*.

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8, claude-opus-4.6 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae |
| Reviewer agent | copilot |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-04T05:12:00Z
**Outcome:** GO

Independent plan-vs-implementation review against the merged content (commit `8fb4106`, PR #69).

| Deliverable | Outcome | Rationale |
|---|---|---|
| STRIDE threat-model doc | match | `docs/security/threat-model.md` defines assets, 10 trust boundaries, full STRIDE sections, cited+verified controls, residual risks, and follow-ons. |
| Mitigations: tuple/policy tampering, confused-deputy, token replay/forgery | match | Tuple/policy tampering covered as residual risk + tracked follow-on (R1 amendment); confused-deputy documented (token/resource-row binding); token replay/forgery documented AND hardened in both JWT setups (30s ClockSkew, RequireExpirationTime, RequireSignedTokens, ValidateIssuerSigningKey). |
| Fail-closed defaults on PDP outage; secrets management; least-privilege review | match | Fail-closed documented + backed by existing PDP/entitlements/governance tests; `docs/security/secrets-and-least-privilege.md` provides a secrets inventory, dev/prod handling guidance, least-privilege review, and hardening checklist. |

**Test-coverage:** sufficient — new Bank.Api + Edge.Gateway token-security tests assert the hardened JWT config and functionally reject expired / tampered / wrong-audience / wrong-issuer / missing-exp / old-default-skew tokens while accepting a valid token; existing tests cover fail-closed PDP adapter, entitlements-outage, and governance SoD/PDP-outage behavior. No material CS18 coverage gaps. Full solution 784/784.
