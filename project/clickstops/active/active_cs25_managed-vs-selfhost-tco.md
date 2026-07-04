# CS25 — Managed-vs-self-host TCO + cloud move

**Status:** active
**Owner:** yoga-ae-c4
**Branch:** cs25/content
**Started:** 2026-07-04
**Closed:** —
**Phase:** 6 — Evaluation lab
**Lane:** Eval
**Depends on:** CS23, CS24

## Goal

Analyze managed-vs-self-host trade-offs and what changes moving to the cloud.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 7aa4a05cf3ce | 2026-07-02T19:47:54Z | Go | CS24 is now included, directly and via CS23, so TCO can use benchmark sizing data. |

## Deliverables

- TCO/ops comparison across managed offerings (Auth0 FGA, AuthZed Cloud, Oso Cloud, Permit.io, Amazon Verified Permissions) vs self-hosted OSS.
- Migration / cloud-move considerations feeding the Azure deployment (CS27).

## Exit criteria

- Documented TCO + cloud-move guidance per option, cross-referenced to the matrix and ADRs.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Gather managed-vs-selfhost data | done | tco-scribe | agent-id=tco-scribe \| role=implementer \| report-status=complete \| learnings=1 |
| Cost/ops analysis | done | tco-scribe | agent-id=tco-scribe \| role=implementer \| report-status=complete \| learnings=1 |
| Cloud-move considerations | done | tco-scribe | agent-id=tco-scribe \| role=implementer \| report-status=complete \| learnings=0 — Azure cloud-move section feeding CS27 |
| ADR | done | cs25-crossref | ADR `docs/adr/0007-self-host-first-authz-with-managed-optionality.md` authored + cross-refs wired after CS23 merged (PR #111); agent-id=cs25-crossref \| role=implementer \| report-status=complete \| learnings=0 |
| Close-out: docs + restart state | pending | — | Update WORKBOARD, CONTEXT.md, and the TCO/cloud-move docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md; open follow-up CSs for unresolved TCO/cloud-move gaps |

## Notes / Learnings

- **Deliverable:** `docs/eval/managed-vs-selfhost-tco.md` (TCO/ops comparison of the 5 managed offerings vs self-hosted OSS + Azure cloud-move section feeding CS27) and ADR `docs/adr/0007-self-host-first-authz-with-managed-optionality.md`, cross-referenced with CS23's `comparison-matrix.md` / `market-survey.md` (bidirectional).
- **Sequencing:** claimed while CS23 was in flight and started the independent TCO research (deferring matrix/ADR cross-refs). CS23 merged (PR #111) during implementation, so the deferred ADR + cross-refs were completed in the same content PR rather than a follow-up.
- **Learning candidate (to file at close-out):** authz-SaaS pricing is largely contact-sales/aggregator-sourced and volatile — author eval-lab TCO docs at the pricing-model + cost-driver level with a dated honesty caveat + Sources section, not stale exact figures.

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c4 |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
