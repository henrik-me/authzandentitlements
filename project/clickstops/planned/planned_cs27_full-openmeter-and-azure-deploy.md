# CS27 — Full OpenMeter metering + Azure deployment

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** 7 — Expansion + Azure
**Lane:** Expansion
**Depends on:** CS10, CS12, CS14, CS15, CS25

## Goal

Add full usage metering and cloud deployment.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 5a1fc206ed79 | 2026-07-02T19:47:54Z | Go | Dependencies now cover OpenMeter, observability, full app surfaces, and CS25 cloud-move inputs. |

## Deliverables

- Full OpenMeter (Kafka + ClickHouse) replacing the lightweight metering.
- azd -> Azure Container Apps manifests for the whole Aspire app.
- Local-vs-cloud differences documented.

## Exit criteria

- OpenMeter meters usage end-to-end; `azd up` provisions the app to Azure Container Apps.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Integrate OpenMeter infra | pending | — | |
| Wire metering | pending | — | |
| Author azd manifests | pending | — | |
| Document local-vs-cloud | pending | — | |

## Notes / Learnings

_None yet — populated during implementation and close-out._
