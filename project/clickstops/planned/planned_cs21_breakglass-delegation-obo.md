# CS21 — Break-glass, delegation & on-behalf-of

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** Cross-cutting
**Lane:** Cross-cutting
**Depends on:** CS05, CS11, CS13, CS14, CS19

## Goal

Provide emergency break-glass access and delegation with full mechanism AND process (high-risk). Reuses the on-behalf-of mechanism from CS19.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | f03f079409b9 | 2026-07-02T19:47:54Z | Go | Now depends on CS19, reuses its OBO mechanism explicitly, and dependency graph is acyclic. |

## Deliverables

- Break-glass access: heightened audit + auto-expiry + mandatory post-review.
- Delegation (manager -> delegate); on-behalf-of integration.
- PDP + Governance enforcement; product UX + runbook.

## Exit criteria

- A break-glass grant works, auto-expires, and forces post-review; delegation + OBO enforced and audited.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Break-glass grant + expiry + review | pending | — | |
| Delegation model | pending | — | |
| OBO enforcement | pending | — | |
| UX + runbook | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
