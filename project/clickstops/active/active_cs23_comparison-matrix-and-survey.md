# CS23 — Comparison matrix + market survey

**Status:** active
**Owner:** yoga-ae-c2
**Branch:** cs23/content
**Started:** 2026-07-04
**Closed:** —
**Phase:** 6 — Evaluation lab
**Lane:** Eval
**Depends on:** CS15, CS24

## Goal

Produce the evaluation-lab deliverables: comparison matrix + broad market survey + ADRs.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | b5dbfef80671 | 2026-07-02T19:47:54Z | Go | CS24 is now included, so the matrix and ADRs can be grounded in benchmark data beyond CS15 qualitative evidence. |

## Deliverables

- Comparison matrix (models, consistency, latency, reverse-index, policy language, testability, auditability, ops burden, .NET support, AuthZEN alignment, licensing/maturity, hosting).
- Survey docs: OpenFGA, SpiceDB, Cerbos, OPA, Cedar/AVP, Keto, Oso, Topaz, Permify, Casbin, Warrant/WorkOS, Permit.io; entitlements: OpenMeter, Stigg, OpenFeature, Flagsmith, Unleash, Entra Governance; + AuthZEN.
- ADRs + strengths/weaknesses/when-to-use.

## Exit criteria

- Matrix populated from real integrations; survey + ADRs published in-repo.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Comparison matrix from real integrations | done | cs23-matrix | `docs/eval/comparison-matrix.md` (12 dimensions; consistency + licensing/maturity added in follow-up PR #117) — agent-id=cs23-matrix \| role=implementer \| report-status=complete \| learnings=0 |
| Survey: ReBAC/Zanzibar family | done | cs23-survey-rebac | `docs/eval/survey/relationship-based-zanzibar.md` (incl. strengths/weaknesses/when-to-use) — agent-id=cs23-survey-rebac \| role=implementer \| report-status=complete \| learnings=1 |
| Survey: policy + decision engines | done | cs23-survey-policy | `docs/eval/survey/policy-and-decision-engines.md` (incl. strengths/weaknesses/when-to-use) — agent-id=cs23-survey-policy \| role=implementer \| report-status=complete \| learnings=1 |
| Survey: entitlements + AuthZEN | done | cs23-survey-entitlements | `docs/eval/survey/entitlements-and-flags.md` + `docs/eval/survey/authzen.md` — agent-id=cs23-survey-entitlements \| role=implementer \| report-status=complete \| learnings=0 |
| Survey index / taxonomy | done | cs23-survey-index | `docs/eval/market-survey.md` — agent-id=cs23-survey-index \| role=implementer \| report-status=complete \| learnings=0 |
| ADRs (decisions + when-to-use) | done | cs23-adr | `docs/adr/` index + 6 ADRs — agent-id=cs23-adr \| role=implementer \| report-status=complete \| learnings=2 |
| Close-out: docs + restart state | done | yoga-ae-c2 | WORKBOARD row removed; CONTEXT.md updated (CS23 done + next-claimable); `docs/eval/` + `docs/adr/` are the restart surface |
| Close-out: learnings + follow-ups | done | yoga-ae-c2 | LEARNINGS.md updated (parallel-lint CRLF race; ADR-structure extension; Copilot wording ping-pong); no blocking follow-ups |

## Notes / Learnings

**Implementation (2026-07-04).** Six parallel background sub-agents (Claude Opus 4.8; entitlements
ran on the Opus 4.7 fallback) produced the evaluation-lab docs on `cs23/content`: the grounded
comparison matrix (`docs/eval/comparison-matrix.md`), the market-survey index + four category
deep-dives (`docs/eval/survey/{relationship-based-zanzibar,policy-and-decision-engines,entitlements-and-flags,authzen}.md`),
and the ADR set (`docs/adr/README.md` + `0001..0006`). Matrix integrated-engine cells are grounded in
shipped code/docs (`reference/aspnet/casbin/cedar/opa/openfga` + OpenFeature/Unleash) and the CS24
benchmark baseline; surveyed engines are secondary research with per-item Sources. Content PR #111
(squash `cf98afd`). The GPT-5.5 rubber-duck review caught real fact-claim inaccuracies (OpenFGA
"zookie-style"/token consistency overstated; AuthZEN response field named `decision` not `allow`;
obligations ride in AuthZEN `context`), all fixed before merge. The GPT-5.5 plan-vs-impl close-out
gate then found the integrated matrix covered only 10/12 named dimensions — a follow-up PR #117
(squash `fcbbdc8`) added the `Consistency` + `Licensing / maturity` rows, after which the gate is GO.
`harness lint` 22/0 throughout; a link-target check confirmed 157/157 relative links resolve.

**Learnings filed (see LEARNINGS.md):** parallel doc sub-agents trip each other's whole-tree
`harness lint` text-encoding self-check on transient CRLF (normalize + re-lint at wave end);
project ADRs extend the base CONVENTIONS structure with `Alternatives considered` + `When to use`
(documented in the CONVENTIONS project-local block); and Copilot content-review on a docs PR tends
to surface successive minor wording-consistency nits — resolve non-blocking ones rather than
re-spinning indefinitely.

**Follow-ups:** none blocking. Optional future polish: deepen surveyed-engine matrix cells (currently
summary-level with links to the sub-docs).

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8, claude-opus-4.7 |
| Reviewer model | gpt-5.5 |
| Implementer agent | cs23-matrix, cs23-survey-rebac, cs23-survey-policy, cs23-survey-entitlements, cs23-survey-index, cs23-adr |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-04T21:52:10Z
**Outcome:** GO

Per-deliverable outcome:

| Deliverable | Outcome | Rationale |
|---|---|---|
| D1 — comparison matrix (12 dimensions, real integrations) | match | `docs/eval/comparison-matrix.md` includes all 12 named dimensions for integrated authorization engines, including the initially-missing `Consistency` and `Licensing / maturity` rows (added in PR #117). Latency is split into cold/warm percentile rows grounded in the committed benchmark baseline; integrated-engine claims cite shipped adapters/docs. |
| D2 — market survey (authz + entitlements + AuthZEN, strengths/weaknesses/when-to-use) | match | `market-survey.md` indexes every named authorization engine, entitlement/flag product, and AuthZEN. The survey sub-docs cover each named item with strengths, weaknesses, and when-to-use guidance. |
| D3 — ADRs + when-to-use | match | `docs/adr/README.md` indexes ADRs 0001–0006; each records an accepted shipped decision with realized-in clickstops plus `When to use / when not` guidance. |
| Exit criteria — matrix from real integrations; survey + ADRs published | match | The matrix is published in-repo and distinguishes grounded integrated engines from surveyed engines; survey index/sub-docs and the ADR index/records are present under `docs/eval` and `docs/adr`. |

**Coverage:** All named engines/products and all 12 required comparison dimensions are covered; consistency and licensing/maturity are present and grounded.

**Outcome GO:** The prior blocking gap (2 missing matrix dimensions) is fixed; deliverables and exit criteria are met. Remaining scope choices — summary-level surveyed-engine matrix cells vs deeper sub-doc detail — are documented and non-blocking.
