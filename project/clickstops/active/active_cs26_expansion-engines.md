# CS26 — Expansion engines (SpiceDB/Cerbos/Keto/Oso/Topaz)

**Status:** active
**Owner:** yoga-ae-c4
**Branch:** cs26/content
**Started:** 2026-07-04
**Closed:** —
**Phase:** 7 — Expansion + Azure
**Lane:** Expansion
**Depends on:** CS05, CS15

## Goal

Broaden engine coverage by adding more adapters behind the same abstraction/playground.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 72b99c393582 | 2026-07-02T19:47:54Z | Go-with-amendments | Soften benchmark exit or add CS24; PDP and playground deps cover adapters but benchmark appearance otherwise races CS24. |

## Deliverables

- Adapters + AppHost containers for SpiceDB, Cerbos, Ory Keto, Oso, Topaz/Aserto.
- Each plugged into the scenario catalog + playground.
- Notes on the SpiceDB-vs-OpenFGA head-to-head.

## Exit criteria

- Expansion engines answer the scenario catalog and appear in the playground/benchmarks.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Add SpiceDB | pending | — | |
| Add Cerbos | pending | — | |
| Add Keto/Oso/Topaz | pending | — | |
| Wire into catalog + playground | pending | — | |
| Close-out: docs + restart state | pending | — | Update WORKBOARD, CONTEXT.md, and the engine-adapter/playground docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md; open follow-up CSs for unresolved expansion-engine gaps |

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
