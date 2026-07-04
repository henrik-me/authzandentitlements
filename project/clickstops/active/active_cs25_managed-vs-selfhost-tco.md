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
| Gather managed-vs-selfhost data | pending | — | |
| Cost/ops analysis | pending | — | |
| Cloud-move considerations | pending | — | |
| ADR | pending | — | |
| Close-out: docs + restart state | pending | — | Update WORKBOARD, CONTEXT.md, and the TCO/cloud-move docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md; open follow-up CSs for unresolved TCO/cloud-move gaps |

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

> _(filled at close-out per the gate)_
