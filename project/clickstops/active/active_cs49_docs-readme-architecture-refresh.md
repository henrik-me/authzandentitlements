# CS49 — Refresh README + ARCHITECTURE to shipped reality (usage, architecture, data flows)

**Status:** active
**Owner:** yoga-ae-c3
**Branch:** cs49/docs-readme-architecture-refresh
**Started:** 2026-07-04
**Closed:** —
**Filed by:** yoga-ae-c3, 2026-07-04 — user request: "update readme and architecture .md files to reflect how to use the product/lab, as well as document the system architecture, and key data flows".
**Phase:** Maintenance (docs)
**Lane:** Docs
**Depends on:** none

## Goal

Bring `README.md` and `ARCHITECTURE.md` in line with the **shipped** system. Both
files still describe the pre-implementation bootstrap state (`README.md`: "Nothing
is implemented yet"; `ARCHITECTURE.md`: "Last updated 2026-07-02 (bootstrap)"),
but 35 clickstops are done across 11 projects (CS26 expansion engines still in flight). Update them
to document (a) how to run
and use the product/lab, (b) the current system architecture, and (c) the key
data flows — accurately and with no overclaiming of unshipped work.

## Background

35 clickstops are complete (see `CONTEXT.md`). The lab is a runnable .NET Aspire
solution with a four-layer authorization stack (AuthN → coarse edge → fine PDP →
entitlements) plus a hash-chained audit log, eight integrated PDP engines
(`reference`/`aspnet`/`casbin`/`cedar` in-process; `opa`/`openfga`/`spicedb`/`cerbos`
container-backed opt-in), governance (JIT/reviews/break-glass/delegation), a Blazor
product UI (workflows + AuthZ Playground + Audit Explorer), an observability stack,
and an evaluation-lab documentation set (comparison matrix, market survey, ADRs,
benchmarks, TCO, compliance mapping). The two flagship docs are the primary
onboarding surface and are materially stale.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Scope | `README.md` + `ARCHITECTURE.md` only — no code, no other docs | Keep the change focused and reviewable; the `docs/**` set is current and largely CS-owned. |
| 2 | Framing | Keep the four-layer + eval-lab framing; update facts to shipped reality | The framing is still correct; only the "not implemented" status, engine list, and wiring drifted. |
| 3 | Engine set | Document the 8 engines integrated on `main`; mark Keto/Oso/Topaz as *planned* expansion | SpiceDB (PR #134) + Cerbos (PR #139) are merged; Keto/Oso/Topaz remain pending in CS26 / CS46 / CS47. |
| 4 | Run instructions | `aspire run` from `src/AuthzEntitlements.AppHost`; document default critical-path services, opt-in engines (`WithExplicitStart`), ports (Keycloak 8088, Grafana 3000, OTLP 4317/4318), seeded users, and the build/test commands | Give a copy-pasteable getting-started grounded in `AppHost.cs`. |
| 5 | Diagrams | Update the ARCHITECTURE mermaid (add SpiceDB/Cerbos, the observability container, the audit-forwarding path, Bank.Web edges) and add key data-flow diagrams | The current diagram is stale and incomplete. |
| 6 | No overclaiming | Cite shipped surfaces only; mark Azure/CS27, full OpenMeter, and Keto/Oso/Topaz as forward-looking | Fact-claim accuracy per REVIEWS.md § 2.6a. |
| 7 | Process | Track as CS49 in a single consolidated PR (claim + content), left for user review; do not self-merge | User unavailable; respects the branch-protection posture. |

## Deliverables

- **`README.md`** rewritten: what-it-is + four-layer summary; prerequisites; quickstart
  (`aspire run`); how-to-use (log in as a seeded user, run a maker-checker flow, switch
  PDP engines, use the Playground + Audit Explorer, reach Grafana); build/test commands;
  an evaluation-lab deliverables index; a documentation map; a current status/process
  pointer; license.
- **`ARCHITECTURE.md`** rewritten: current overview + updated "last updated" line; the
  11-project component list; an updated top-level mermaid diagram; the four-layer + audit
  model; the PDP engine-adapter seam (8 engines + parity harness); 3–5 **key data-flow**
  diagrams (transaction POST end-to-end; standalone PDP evaluate + scenario parity; audit
  append + verify; JIT/SoD governance; engine swap); the data-store table (single Postgres,
  six logical DBs); observability wiring; security posture; an updated decision log
  (harness pin v0.16.0); the eval-lab framing; and a status/roadmap reflecting done vs.
  remaining work.
- **CS49 clickstop record** (this file). _(A `WORKBOARD.md` Active Work row was planned but descoped
  during implementation to keep this docs PR immune to concurrent WORKBOARD churn; the divergence is
  recorded here and at close-out.)_
- Gates green: `harness lint` 0 failed; `harness sync --mode=check` no drift; LF / no-BOM.
- An independent GPT-5.5 (rubber-duck) content review recorded in the PR body.

## Exit criteria

- `README.md` and `ARCHITECTURE.md` accurately describe the shipped system, how to run
  and use it, and the key data flows, with no claims about unshipped work.
- `harness lint` exits 0 and `harness sync --mode=check` reports no drift.
- The content PR carries the required review-evidence sections and an independent review.

## Risks + open questions

- **Fact drift:** other agents (CS26/CS40) are actively changing `main`; keep claims to
  shipped surfaces and re-verify against `main` before opening the PR.
- **Consolidated claim+content PR** deviates from the three-PR shape — justified by user
  unavailability; documented in the PR body. Close-out (PVI + `active → done` + WORKBOARD
  removal) follows after merge.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | yoga-ae-c3 (rubber-duck) | b2766671cdde | 2026-07-05T03:29:07Z | Go-with-amendments | Sound scope; distinguish integrated/default/opt-in/planned engines, separate default-vs-opt-in runtime (Docker), source-check the 6-DB claim, add opt-in-engine exercise steps. |
| R2 | GPT-5.5 | Claude Opus 4.8 | yoga-ae-c3 (rubber-duck) | 14f4ef6d5fb0 | 2026-07-05T04:56:27Z | Go | Re-attest after WORKBOARD-row descope (PR kept README/ARCHITECTURE-only, immune to WORKBOARD churn); no other Decisions/Deliverables inaccuracies. |

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Research current system state (arch + run/usage) | done | yoga-ae-c3 | Two background explore agents + CONTEXT.md + AppHost.cs cross-check |
| Rewrite README.md | in-progress | yoga-ae-c3 | agent-id=yoga-ae-c3 \| role=docs \| report-status=in-progress \| learnings=0 |
| Rewrite ARCHITECTURE.md | in-progress | yoga-ae-c3 | agent-id=yoga-ae-c3 \| role=docs \| report-status=in-progress \| learnings=0 |
| Close-out: docs + restart state | pending | yoga-ae-c3 | Update WORKBOARD + CONTEXT.md after merge so a fresh agent restarts from actual state |
| Close-out: learnings + follow-ups | pending | yoga-ae-c3 | File/disposition learnings in LEARNINGS.md; open follow-up CSs for any unresolved doc gaps |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c3 |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
