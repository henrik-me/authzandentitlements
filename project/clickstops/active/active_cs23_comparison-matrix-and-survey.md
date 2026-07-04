# CS23 — Comparison matrix + market survey

**Status:** active
**Owner:** yoga-ae-c2
**Branch:** cs23/content
**Started:** 2026-07-04
**Closed:** —
**Phase:** 6 — Evaluation lab
**Lane:** Eval
**Depends on:** CS15, CS24

## Goal

Produce the evaluation-lab deliverables: comparison matrix + broad market survey + ADRs.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | b5dbfef80671 | 2026-07-02T19:47:54Z | Go | CS24 is now included, so the matrix and ADRs can be grounded in benchmark data beyond CS15 qualitative evidence. |

## Deliverables

- Comparison matrix (models, consistency, latency, reverse-index, policy language, testability, auditability, ops burden, .NET support, AuthZEN alignment, licensing/maturity, hosting).
- Survey docs: OpenFGA, SpiceDB, Cerbos, OPA, Cedar/AVP, Keto, Oso, Topaz, Permify, Casbin, Warrant/WorkOS, Permit.io; entitlements: OpenMeter, Stigg, OpenFeature, Flagsmith, Unleash, Entra Governance; + AuthZEN.
- ADRs + strengths/weaknesses/when-to-use.

## Exit criteria

- Matrix populated from real integrations; survey + ADRs published in-repo.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Build matrix from results | pending | — | |
| Author survey docs | pending | — | |
| Write ADRs | pending | — | |
| Strengths/weaknesses/when-to-use | pending | — | |
| Close-out: docs + restart state | pending | — | Update WORKBOARD, CONTEXT.md, and the eval docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md; open follow-up CSs for unresolved survey/matrix gaps |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c2 |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
