# CS08 — Adapter: OPA / Rego (policy / ABAC)

**Status:** done
**Owner:** yoga-ae
**Branch:** cs08/content
**Started:** 2026-07-03
**Closed:** 2026-07-04
**Phase:** 2 — Fine-grained AuthZ
**Lane:** Engines
**Depends on:** CS05

## Goal

Integrate OPA/Rego for maker-checker, segregation-of-duties, and conditional policy scenarios.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 3de3961da0b7 | 2026-07-02T19:47:54Z | Go | Sound as-is: CS05 dependency, Rego maker-checker, SoD, threshold, and condition scenarios are coherent. |

## Deliverables

- OPA Aspire container (REST decision API).
- Rego policies: maker-checker (creator != approver), four-eyes/dual-auth thresholds, SoD, conditions on amount/time/geo/risk/tier.
- OpaProvider; WASM in-process noted as alternative.

## Exit criteria

- OPA answers the policy scenarios; policies unit-tested with `opa test`.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Author Rego decision policy (mirror reference rules → 22-scenario parity) | done | sub-agent | agent-id=cs08-impl-policy \| role=policy-author \| report-status=complete \| learnings=1 |
| Author `opa test` unit tests for the decision policy | done | sub-agent | agent-id=cs08-impl-policy \| role=policy-author \| report-status=complete \| learnings=1 |
| ABAC conditions showcase (amount/time/geo/risk/tier) + `opa test` | done | sub-agent | agent-id=cs08-impl-policy \| role=policy-author \| report-status=complete \| learnings=1 |
| Add OPA Aspire container (WithExplicitStart, pinned tag, bind-mount policy) + inject endpoint into authz-pdp | done | sub-agent | agent-id=cs08-impl-policy \| role=policy-author \| report-status=complete \| learnings=1 |
| Implement `OpaDecisionProvider` (Name="opa") + DI registration + `Opa` config | done | sub-agent | agent-id=cs08-impl-adapter \| role=adapter-implementer \| report-status=complete \| learnings=1 |
| OpaDecisionProvider unit tests (request shaping, response mapping, fail-closed) | done | sub-agent | agent-id=cs08-impl-adapter \| role=adapter-implementer \| report-status=complete \| learnings=1 |
| Adapter doc `docs/authz/opa-adapter.md` | done | sub-agent | agent-id=cs08-impl-adapter \| role=adapter-implementer \| report-status=complete \| learnings=1 |
| Close-out: docs + restart state | done | yoga-ae | Updated WORKBOARD.md, CONTEXT.md; adapter/policy/bundle docs shipped in #38 + WASM note in #41 |
| Close-out: learnings + follow-ups | done | yoga-ae | Filed LRN-027..029; no follow-up CS needed (CS09 Cedar already planned) |

## Notes / Learnings

_Delivered the OPA/Rego engine adapter for the unified PDP: an out-of-process OPA REST decision server (opt-in Aspire `opa` container, `openpolicyagent/opa:1.18.2-static`, `WithExplicitStart`, off the default `aspire run` critical path), a `authz.bank` Rego policy that mirrors `ReferenceDecisionProvider` exactly (22-scenario parity), a fail-closed `OpaDecisionProvider` (Name `opa`, sync-over-HTTP, sanitized messages, bounded reason-code validation), a bounded ABAC-conditions showcase, and adapter/bundle docs. Content PR #38 (squash-merged); follow-ups #40 (make a CS06 registration test robust to new engines) and #41 (WASM in-process alternative doc note). `dotnet build` 0/0; full solution 404 tests (PDP 264, incl. 30 OPA adapter tests); `opa test` 45/45; live `POST /api/authz/scenarios/verify` 22/22 with `Pdp:Provider=opa`; fail-closed verified. GPT-5.5 rubber-duck (R1–R5) + Copilot (3 rounds, all addressed). New learnings LRN-027..029 (see LEARNINGS.md)._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae |
| Reviewer agent | copilot |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-04T00:29:00Z
**Outcome:** GO

Per-deliverable outcomes:

| # | Deliverable | Outcome |
|---|---|---|
| 1 | OPA Aspire container (REST decision API) | match — `AppHost.cs` opt-in `opa` container (`openpolicyagent/opa:1.18.2-static`, `WithExplicitStart`, no `WaitFor`) bind-mounting `infra/opa/policy`; `Opa__BaseUrl` injected into `authz-pdp` |
| 2 | Rego policies: maker-checker, four-eyes/dual-auth thresholds, SoD, conditions (amount/time/geo/risk/tier) | match — `authz.rego` mirrors `ReferenceDecisionProvider` (ordered checks, reason codes, threshold obligation, NotPending-before-SoD); `amount` drives the live obligation; time/geo/risk/tier delivered as the tested `conditions.rego` policy-layer showcase, documented as beyond the current CS05 contract |
| 3 | OpaProvider; WASM in-process noted as alternative | match — `OpaDecisionProvider` (Name `opa`, sync-over-HTTP, fail-closed `ProviderUnavailable`, bounded reason-code validation) + DI + `Opa` config; the gate flagged the WASM note as a non-blocking follow-up, added in #41 |

**Exit criteria:** both met — OPA answers the full 22-scenario catalog (live `POST /api/authz/scenarios/verify` → 200, AllPassed 22/22 with `Pdp:Provider=opa`); policies unit-tested with `opa test infra/opa/policy` (45/45).

**Test coverage:** sufficient — 45 `opa test` (30 decision-parity incl. all 22 catalog scenarios + 15 conditions showcase) + 30 deterministic C# adapter tests (request shaping, mapping, reason-code validation, every fail-closed path incl. message sanitization + config-exception backstop).

Independently verified on `main`: `dotnet build` 0/0; full solution 404 tests pass (PDP 264). GPT-5.5 rubber-duck content review R1–R5 (all Go) + Copilot (3 comment rounds addressed: fail-closed info-leak, sync-contract comment accuracy, untrusted reason-code validation). The single divergence the gate raised (missing WASM-alternative doc note) was non-blocking and resolved in #41.
