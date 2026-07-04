# CS18 — Security hardening + threat model

**Status:** active
**Owner:** yoga-ae
**Branch:** cs18/content
**Started:** 2026-07-04
**Closed:** —
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
| Author STRIDE TM | pending | — | |
| Implement fail-closed | pending | — | |
| Secrets + least-privilege | pending | — | |
| Add security tests | pending | — | |
| Close-out: docs + restart state | pending | — | Update WORKBOARD.md, CONTEXT.md, and relevant docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md and create planned follow-up CSs for unresolved issues |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae |
| Reviewer agent | copilot |

## Plan-vs-implementation review

_Pending — completed at close-out per OPERATIONS.md § Plan-vs-implementation review (close-out gate). The GO/NEEDS-FIX outcome is recorded here before the active → done rename._
