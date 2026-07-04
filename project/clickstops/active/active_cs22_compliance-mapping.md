# CS22 — Compliance mapping (SOX/PCI-DSS/GDPR)

**Status:** active
**Owner:** yoga-ae-c4
**Branch:** cs22/content
**Started:** 2026-07-04
**Closed:** —
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

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c4 |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

_Pending — populated at close-out per OPERATIONS.md § Plan-vs-implementation review (close-out gate). NEEDS-FIX blocks close-out._
