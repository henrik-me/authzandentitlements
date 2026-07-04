# CS13 — Tamper-evident audit log pipeline

**Status:** done
**Owner:** yoga-ae-c2
**Branch:** cs13/content
**Started:** 2026-07-04
**Closed:** 2026-07-04
**Phase:** 4 — Observability + audit
**Lane:** Observability
**Depends on:** CS05

## Goal

Implement a tamper-evident, append-only audit log covering every authz/entitlement decision.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 295b3f9cb653 | 2026-07-02T19:47:54Z | Go-with-amendments | Blocker resolved; PDP audit rows are deliverable with CS05, but task wording should clarify PDP producer first. |

## Deliverables

- Audit.Service; hash-chained Postgres audit store (prev-hash + payload -> row-hash).
- Ingestion from PDP decisions first; entitlement/JIT/approval producers wire in as CS10/CS11 land.
- Chain-verification endpoint + query API.

## Exit criteria

- PDP decisions produce audit rows; chain verification detects tampering; query API works (other producers wired as they land).

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Design hash-chain schema + store | complete | yoga-ae-c2 | agent-id=yoga-ae-c2/audit-svc \| role=implementer \| report-status=complete \| learnings=0 |
| Writer + ingestion endpoint | complete | yoga-ae-c2 | agent-id=yoga-ae-c2/audit-svc \| role=implementer \| report-status=complete \| learnings=0 |
| Verification + query endpoints | complete | yoga-ae-c2 | agent-id=yoga-ae-c2/audit-svc \| role=implementer \| report-status=complete \| learnings=0 |
| Wire producers (PDP sink + AppHost) | complete | yoga-ae-c2 | agent-id=yoga-ae-c2/pdp-sink \| role=implementer \| report-status=complete \| learnings=1; AppHost wiring by orchestrator |
| Close-out: docs + restart state | complete | yoga-ae-c2 | WORKBOARD row removed, CONTEXT.md updated, docs/authz/audit-pipeline.md shipped |
| Close-out: learnings + follow-ups | complete | yoga-ae-c2 | LRN-041 (DropWrite/TryWrite) filed; CS13×CS16 build break documented in LRN-040 (CS11 close-out) |

## Notes / Learnings

- Content PR #57 (feat) + build-unbreak hotfix PR #60. 5-round GPT-5.5 rubber-duck review (R1 No-go → R5 Go) + Copilot (2 rounds) + plan-vs-impl GO.
- **Semantic merge conflict (LRN-040):** CS16 added required fields to `PdpDecisionAuditEvent` while CS13 added a test constructing it; the PRs merged cleanly as text but the combined `main` did not compile (harness CI does not build .NET). Fixed by #60.
- **Follow-up candidates (dispositioned as learnings for weekly harvest, not filed as CSs):** (a) a `dotnet build`/`dotnet test` CI step or merge-queue so concurrent PRs can't red main (LRN-040); (b) capture CS16 explainability fields (`determiningRule`/`policyReferences`/`narrative`) in the audit store — already on the wire, currently dropped by the ingest DTO; (c) external anchoring for full rewrite-prevention (documented out-of-scope in `audit-pipeline.md`).

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8, claude-opus-4.7 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c2 |
| Reviewer agent | rubber-duck, copilot |

## Plan-vs-implementation review

**Reviewer:** gpt-5.5 (rubber-duck, agent `cs13-plan-vs-impl`) — independent of the claude-opus-4.8/4.7 implementers
**Date:** 2026-07-04
**Outcome:** GO

All three deliverables and the exit criteria are faithfully satisfied with no scope drift:
- **D1 — Audit.Service + hash-chained Postgres store:** `AuthzEntitlements.Audit.Service` persists decisions to the `audit` DB via EF/Npgsql (`AuditEntry`/`AuditDbContext` + `InitialCreate`); `AuditHashChain` implements the SHA-256 prev-hash + payload → row-hash chain (`ComputeRowHash`/`Append`/`Verify`/`VerifyAsync`).
- **D2 — PDP ingestion first:** `PdpDecisionService.Evaluate` emits one `PdpDecisionAuditEvent`; `HttpForwardingPdpDecisionAuditSink` + `AuditForwardingWorker` forward it off the decision hot path; `AppHost.cs` wires `Audit__Sink=http` + the service URL.
- **D3 — verification + query APIs:** `AuditEndpoints` maps `POST /api/audit/decisions`, `GET /api/audit/verify` (+ optional trusted checkpoint), `GET /api/audit/entries` (filters/paging).
- **Exit criteria:** tamper detection is proven by `AuditHashChainTests` (all tamper classes + checkpoint truncation/rewrite); the query API and runtime Postgres path are implemented for the full `aspire run` stack. The trusted-checkpoint + fail-closed hardening added during review strengthen tamper-evidence rather than drift from scope.
