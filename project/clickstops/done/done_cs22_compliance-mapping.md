# CS22 — Compliance mapping (SOX/PCI-DSS/GDPR)

**Status:** done
**Owner:** yoga-ae-c4
**Branch:** cs22/content
**Started:** 2026-07-04
**Closed:** 2026-07-04
**Phase:** Cross-cutting
**Lane:** Cross-cutting
**Depends on:** CS11, CS12, CS13

## Goal

Map the lab’s controls to regulatory frameworks and surface evidence.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 4ec78333b179 | 2026-07-02T19:47:54Z | Go | CS12 is now included, so Grafana compliance dashboards have the needed observability base. |

## Deliverables

- SOX/PCI-DSS/GDPR control-mapping doc.
- Audit retention + tamper-evidence; access-certification evidence; SoD reporting; least-privilege attestations.
- Grafana compliance dashboards.

## Exit criteria

- Each mapped control has evidence produced by the system; SoD + certification reports available.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Author control mapping | done | yoga-ae-c4 | `docs/compliance/control-mapping.md` (176 lines): one table per framework (SOX/PCI-DSS/GDPR) → shipped controls, verified file:line, evidence surfaces + honest gaps. agent-id=cs22-docs \| role=implementer \| report-status=complete \| learnings=1 |
| Retention + evidence | done | yoga-ae-c4 | Audit-integrity report (pure `AuditHashChain`: mutation/truncation+checkpoint/gap/prev-hash detection) + least-privilege attestation (live-probe self-skip) in the compliance tool. agent-id=cs22-tool \| role=implementer \| report-status=complete \| learnings=2 |
| SoD + certification reports | done | yoga-ae-c4 | SoD report (reference PDP + `GovernanceSodPolicy`, all 5 pairs) + access-certification report (live-probe self-skip) in the compliance tool. agent-id=cs22-tool \| role=implementer \| report-status=complete \| learnings=2 |
| Compliance dashboards | done | yoga-ae-c4 | Grafana `compliance.json` (185 lines, 8 panels): SoD denials, governance decisions/grants/reviews, PDP outcomes, entitlements. agent-id=cs22-dashboard \| role=implementer \| report-status=complete \| learnings=1 |
| Close-out: docs + restart state | pending | — | Update WORKBOARD, CONTEXT.md, and feature docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md; open planned follow-up CSs for unresolved issues |

## Notes / Learnings

Implemented as (1) `docs/compliance/control-mapping.md` — one table per framework (SOX ITGC / PCI-DSS v4.0 Req 7/8/10 / GDPR Art 5/25/30/32) → shipped control with verified `file:line` citations + evidence surfaces + honest residual gaps; (2) `AuthzEntitlements.Compliance` — a standalone console+library evidence report generator (zero new deps, CS24-Benchmarks pattern): deterministic/DB-free **SoD report** (drives `GovernanceSodPolicy` + in-process `ReferenceDecisionProvider` over all 5 incompatible pairs) and **audit-integrity report** (pure `AuditHashChain`: detects mutation / tail-truncation-with-checkpoint / gap / broken-prev-hash), plus **access-certification** and **least-privilege** reports that live-probe Governance.Service and self-skip offline while failing closed on a reached error; JSON (frozen options, fail-closed reader) + Markdown output; 64 tests; (3) `infra/observability/grafana/dashboards/compliance.json` — Grafana compliance dashboard grounded in the emitted meters. Content PR #83 (squash `6220a13`). Review: GPT-5.5 rubber-duck R1 Needs-Fix (fail-open live probe + 2 doc citations) → R2 Go → R3 Go; Copilot (2 findings: caller-cancellation semantics + GDPR Article 32 wording, both fixed); plan-vs-impl GO. `dotnet build` 0/0, full solution `dotnet test` 1023/0, `harness lint` 22/0. New learnings LRN-050..052.

Follow-ups (non-blocking): the dashboard's non-SoD governance/entitlements panels use metric names inferred from the OTLP→Prometheus mangling convention (verified against the C# meter sources, not scrape-verified — LRN-051); the certification/least-privilege reports are live-probe (not deterministic) because that evidence is DB-backed.

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c4 |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-04T18:29:36Z
**Outcome:** GO

Per-deliverable outcome:

| Deliverable | Outcome | Rationale |
|---|---|---|
| SOX/PCI-DSS/GDPR control-mapping doc | match | `docs/compliance/control-mapping.md` maps all three frameworks with cited controls, evidence surfaces, and explicit residual gaps. |
| Audit retention + tamper-evidence; access-certification; SoD reporting; least-privilege attestations | match | SoD and audit-integrity reports are deterministic; certification and least-privilege are live probes with offline self-skip + reproduction commands — an acceptable divergence for DB-backed governance evidence. |
| Grafana compliance dashboards | match | `infra/observability/grafana/dashboards/compliance.json` covers SoD denials, governance decisions/grants/reviews, PDP, entitlements, and gateway decision metrics. |
| Exit criteria — each mapped control has evidence produced by the system; SoD + certification reports available | match | The mapping points to system endpoints/reports/dashboards, and the compliance tool produces SoD + certification report sections. |

**Test coverage:** sufficient — `AuthzEntitlements.Compliance.Tests` 64/0; full solution `dotnet test` 1023/0.

**Outcome GO:** All three deliverables and the exit criteria match the plan. SoD + audit-integrity evidence is deterministic/DB-free (drives `GovernanceSodPolicy` + `ReferenceDecisionProvider` and the pure `AuditHashChain`); certification + least-privilege are documented live-probe reports that self-skip offline and fail closed on a reached error. Independent GPT-5.5 rubber-duck R1 (Needs-Fix) → R2 (Go) → R3 (Go) + Copilot (2 findings, resolved).
