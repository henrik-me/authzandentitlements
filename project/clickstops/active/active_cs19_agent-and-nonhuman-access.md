# CS19 — Agent + non-agent access

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** Cross-cutting
**Lane:** Cross-cutting
**Depends on:** CS03, CS05, CS13, CS14

## Goal

Authorize AI agents / MCP tools / workload identities alongside humans, with on-behalf-of delegation. The OBO mechanism is defined here and reused by CS21 (no duplication).

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 331dc47bee31 | 2026-07-02T19:47:54Z | Go | Owns OBO definition, references CS21 reuse, depends only on prerequisites, and does not create a reverse edge. |

## Deliverables

- Workload/client-credentials identities in Keycloak; scoped, time-boxed agent tokens.
- On-behalf-of (agent acts for a user) flow.
- PDP scenarios for non-human subjects; both human and agent paths showcased.

## Exit criteria

- An agent identity performs a scoped, audited action on behalf of a user; the human path is unaffected.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Define agent identities/scopes | pending | — | |
| On-behalf-of flow | pending | — | |
| PDP non-human scenarios | pending | — | |
| Product showcase | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
