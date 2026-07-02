# CS27 — Full OpenMeter metering + Azure deployment

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Phase:** 7 — Expansion + Azure
**Lane:** Expansion
**Depends on:** CS10, CS12

## Goal

Add full usage metering and cloud deployment.

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
