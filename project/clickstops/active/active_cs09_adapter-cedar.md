# CS09 — Adapter: Cedar (policy / ABAC)

**Status:** active
**Owner:** yoga-ae-c4
**Branch:** cs09/content
**Started:** 2026-07-04
**Closed:** —
**Phase:** 2 — Fine-grained AuthZ
**Lane:** Engines
**Depends on:** CS05

## Goal

Integrate Cedar (in-process via MonoCloud Cedar for .NET) as a second policy engine to compare against OPA.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 2223f626be57 | 2026-07-02T19:47:54Z | Go-with-amendments | Clarify Cedar parity is against the CS05 shared policy catalog, not CS08 artifacts, to preserve parallelism. |

## Deliverables

- CedarProvider using MonoCloud Cedar for .NET (verify .NET 10 compat).
- Cedar schema + policies mirroring the OPA scenarios.
- Amazon Verified Permissions documented as the cloud option.

## Exit criteria

- Cedar answers the same policy scenarios as OPA for head-to-head comparison.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Add MonoCloud Cedar | pending | — | |
| Author schema + policies | pending | — | |
| Implement adapter | pending | — | |
| Verify parity with OPA scenarios | pending | — | |
| Close-out: docs + restart state | pending | yoga-ae-c4 | Update WORKBOARD.md, CONTEXT.md, and relevant docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | yoga-ae-c4 | File/disposition learnings in LEARNINGS.md and create planned follow-up CSs for unresolved issues |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c4 |
| Reviewer agent | copilot |

## Plan-vs-implementation review

_Pending — completed at close-out per OPERATIONS.md § Plan-vs-implementation review (close-out gate). The GO/NEEDS-FIX outcome is recorded here before the active → done rename._
