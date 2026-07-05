# CS47 — Oso disposition: de-scope from the expansion-engine set

**Status:** active
**Owner:** yoga-ae-c4
**Branch:** cs47/content
**Started:** 2026-07-05
**Closed:** —
**Filed by:** yoga-ae-c4 on 2026-07-04 — formalises the Oso recommendation surfaced during CS26; Oso was 1 of the 5 originally-planned expansion engines but the CS26 feasibility research found no viable pinnable, self-hostable, in-process .NET path.
**Phase:** 7 — Expansion + Azure
**Lane:** Expansion
**Depends on:** CS23, CS25

## Goal

Formally **disposition Oso**: record the decision to **de-scope** it from the expansion-engine adapter set (rather than ship an adapter), with a durable, evidence-backed rationale and an explicit re-evaluation trigger — so Oso reads as an intentional, justified exclusion, not an incomplete deliverable.

## Background

- CS26 originally planned five expansion engines (SpiceDB, Cerbos, Keto, Oso, Topaz). SpiceDB + Cerbos shipped (PRs #134, #139); Keto + Topaz are a separate follow-on CS; Oso is dispositioned here.
- **CS26 feasibility research (2026-07-04, verified against nuget.org, the `osohq/oso` repo, and Oso docs):**
  - **No maintained, publicly-discoverable in-process .NET/Polar library (as of 2026-07-04).** `osohq/oso` lists Python, Ruby, Java, Node.js, Go, Rust — C#/.NET is absent; `nuget.org/packages/Oso` is 404. The classic OSS library is additionally in maintenance-only mode (deprecated in favour of Oso Cloud).
  - **The only .NET path is the managed Oso Cloud SDK:** the `OsoCloud` NuGet package (HTTP REST client) talks to the hosted Oso Cloud API **or** a local **dev-server** (`public.ecr.aws/osohq/dev-server`, also a downloadable native binary).
  - **The dev-server is pinnable** (versioned tags, e.g. `:v1.2.3`, per Oso's "Pin Dev Server versions in CI" guidance) **but is a development server** (dev/testing scope; ephemeral state; no production SLA/persistence/support). Production requires a **paid, proprietary Oso Cloud** account. _(Corrected 2026-07-05 during implementation: the original CS26 note claimed the dev-server was `latest`-only/unpinnable — that is inaccurate. See ADR 0008 + Notes/Learnings; the de-scope conclusion is unchanged.)_
- This conflicts with the **self-host-first-with-managed-optionality** decision (**ADR-0007**, from CS25): Oso offers **no self-hostable production path** — no in-process .NET/Polar library and no production-grade self-hostable server (only a development-only dev-server plus the paid managed cloud).

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Implement vs. de-scope | **De-scope** — do not ship an Oso adapter | No maintained in-process .NET/Polar library (`nuget.org/packages/Oso` 404); Oso's only self-hostable artifact is a **development-only** dev-server (pinnable, but vendor-scoped to dev/testing — not a production server) and production is the paid, proprietary managed cloud — i.e. no self-hostable production path, incompatible with the ADR-0007 self-host-first posture. _(Rationale corrected 2026-07-05 — the original "unpinnable/`latest`-only" wording was inaccurate; see Notes/Learnings. De-scope unchanged.)_ |
| 2 | Capture the decision durably | Record it in the **eval docs** (extend `docs/eval/comparison-matrix.md`, `docs/eval/market-survey.md`, AND the detailed survey page `docs/eval/survey/policy-and-decision-engines.md` — superseding its older "local/self-hosted PDP binary" wording) and add a short **ADR** (plus a `docs/adr/README.md` index entry) documenting the de-scope + rationale | Knowledge lives in the repo (not agent memory); updating the detailed survey page too prevents the docs from contradicting the ADR; the index entry makes the decision discoverable + cross-linked from the eval + expansion narrative. |
| 3 | Re-evaluation trigger | Revisit **only if** Oso ships either (a) a maintained in-process .NET/Polar library targeting modern .NET, or (b) a **self-hostable, production-supported** server (not development-only, not paid-cloud-only) | Keeps the door open without committing to a moving/managed-only target; ties re-entry to the exact constraints that block it today. |
| 4 | Scope of this CS | **Docs only** — no adapter code, no NuGet pin, no container | The deliverable is a justified disposition + durable record, not an implementation. |

## Deliverables

- An **ADR** (`docs/adr/0008-oso-descoped-from-expansion-engines.md`) recording the de-scope decision, the evidence (no maintained in-process .NET/Polar library — `nuget.org/packages/Oso` 404; a development-only dev-server that is pinnable but not a production server; production = paid managed cloud), the conflict with the ADR-0007 self-host-first posture, and the re-evaluation trigger (Decision #3); plus a `docs/adr/README.md` index entry for discoverability.
- Eval-doc updates: an Oso row/entry in `docs/eval/comparison-matrix.md` and `docs/eval/market-survey.md`, **and** an update to the detailed survey page `docs/eval/survey/policy-and-decision-engines.md` (superseding its older "local/self-hosted PDP binary" wording), each marking Oso **evaluated → de-scoped** with a one-line reason + a link to the ADR.
- The expansion-engine narrative updated so the shipped/planned expansion set reads **Cerbos + SpiceDB + Keto + Topaz**, with **Oso intentionally excluded** (linked to the ADR) — never as a silent gap. This narrative is carried by **ADR 0008 + the eval docs**; `docs/authz/adding-an-engine-adapter.md` is a generic how-to with no shipped/planned engine roster to update.
- A dated honesty caveat + sources on the pricing/availability claims (per the CS25/LRN-064 eval-doc convention).

## User-approval gates

- None. This is a documentation/decision CS. (If a future maintainer wants Oso implemented despite the constraints — e.g. accepting the development-only dev-server for a lab-only demo — that reopens Decision #1 and is a fresh CS.)

## Exit criteria

- Oso is documented as **intentionally de-scoped** with an evidence-backed rationale, an ADR, eval-doc entries, and an explicit re-evaluation trigger; the expansion-engine set is stated as Cerbos/SpiceDB/Keto/Topaz with Oso excluded; `harness lint` green (text-encoding, xref durability). No adapter code, NuGet pin, or container is added.

## Risks + open questions

- **Staleness.** Oso's product/packaging could change; the dated caveat + the re-evaluation trigger (Decision #3) mitigate this.
- **Cross-link durability.** Durable docs must not link to `active/` clickstops; reference the ADR + the `done/` CS26 record or SHA permalinks (harness lint enforces).
- **Minimal risk otherwise** — docs-only, no runtime surface.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | cs45-47-plan-review (rubber-duck) | f34fc40217d2 | 2026-07-05T02:48:12Z | Go-with-amendments | Amendments: added survey-page + ADR-index deliverables. Hash refreshed 2026-07-05: Decisions/Deliverables corrected for dev-server "latest-only" error; de-scope unchanged (see Notes). |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| ADR `0008-oso-descoped-from-expansion-engines.md` + `docs/adr/README.md` index entry | done | yoga-ae-c4 | ADR 0008 (Nygard) + sorted README index row; de-scope evidence **re-verified 2026-07-05** against primary sources (no in-process .NET/Polar lib — `nuget.org/packages/Oso` 404; dev-server pinnable but vendor-dev-only; production = paid managed cloud); conflicts with ADR-0007 self-host-first; re-eval trigger (Decision #3) |
| Eval-doc updates: `comparison-matrix.md` + `market-survey.md` + `survey/policy-and-decision-engines.md` Oso rows → evaluated → de-scoped w/ one-line reason + ADR link | done | yoga-ae-c4 | marked Oso de-scoped (kept surveyed, no `†`); superseded the survey page's older "local/self-hosted PDP binary" wording; TCO Oso-only consistency fixes |
| Expansion narrative: `docs/authz/adding-an-engine-adapter.md` engine list reads Cerbos + SpiceDB + Keto + Topaz, Oso intentionally excluded → ADR | done | yoga-ae-c4 | `adding-an-engine-adapter.md` has NO shipped/planned engine roster (it is a generic how-to) — the expansion narrative is carried by ADR 0008 + the eval docs instead; Oso is never a silent gap |
| Close-out: docs + restart state | pending | yoga-ae-c4 | update WORKBOARD + CONTEXT.md so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | yoga-ae-c4 | file LRNs; planned follow-up CSs for any unresolved issues |

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c4 |
| Reviewer agent | rubber-duck |

## Notes / Learnings

- **R2 review correction (evidence fix).** The independent GPT-5.5 rubber-duck review (R2) caught a
  factual error inherited from the CS26 feasibility notes: the Oso Cloud **dev-server is not
  `latest`-only / unpinnable**. Verified 2026-07-05 against Oso's docs
  (`osohq.com/docs/develop/local-dev/oso-dev-server` — "Pin Dev Server versions in CI", versioned
  tags like `:v1.2.3`, plus a native binary). The de-scope **conclusion stands** on corrected
  grounds — no in-process .NET/Polar library (`nuget.org/packages/Oso` 404) **and** no self-hostable
  **production** server (the dev-server is vendor-scoped to development/testing; production is the
  paid, proprietary managed Oso Cloud) — but the "unpinnable/`latest`-only" wording was removed from
  ADR 0008 and all three eval docs.
- **Scope boundary.** `managed-vs-selfhost-tco.md` labelled SpiceDB / Cerbos / Keto / Topaz
  "planned (CS26)" although SpiceDB + Cerbos shipped (PRs #134/#139) and Keto + Topaz moved to CS46.
  The two rows **edited in this PR** (the expansion-engine baseline row and the adjacent AuthZed
  Cloud→SpiceDB mapping row) were corrected to accurate status per Copilot review; any residual
  non-Oso staleness in docs this CS did not touch remains a follow-up doc-freshness pass.

## Plan-vs-implementation review

**Reviewer:** gpt-5.5 (rubber-duck, independent plan-vs-implementation sub-agent `cs47-pvi-review`) — independent of the claude-opus-4.8 implementer
**Date:** 2026-07-05T17:45:19Z
**Outcome:** GO

Reviewed the merged CS47 implementation at HEAD `2cc9f7ec832f227711e8958e2e6b84f7148d50f1` / PR #170 against the CS47 plan. Headline result: the disposition is complete, evidence-backed, docs-only, and the one planned narrative-file divergence is explicitly justified and acceptable.

| Deliverable | Outcome | Rationale |
|---|---|---|
| D1 — ADR 0008 + README index | match | ADR 0008 records the de-scope, corrected evidence, ADR-0007 conflict, no adapter/pin/container, sources, and re-eval trigger; ADR README indexes 0008. |
| D2 — eval-doc updates (matrix/market/survey) | match | Comparison matrix, market survey, and policy-engine survey all mark Oso evaluated → de-scoped, link ADR 0008, and replace the old self-host wording with dev-server-is-development-only wording. |
| D3 — expansion narrative (Oso excluded, not a silent gap) | diverged | Plan named `adding-an-engine-adapter.md`, but that file has no roster; ADR 0008 + eval/TCO docs carry the Cerbos/SpiceDB/Keto/Topaz narrative and explicitly exclude Oso, so the divergence is acceptable. |
| D4 — dated honesty caveat + sources | match | ADR 0008 has 2026-07-05 availability verification and primary sources; TCO retains dated pricing caveat/sources, including Oso Cloud. |
| Exit criteria | match | Oso is intentionally de-scoped with rationale, ADR, eval entries, and re-eval trigger; expansion set is stated with Oso excluded; `harness lint` passed; HEAD changes are docs/clickstop only. |

**Factual accuracy:** Corrected rationale is honest and consistent: no in-process .NET/Polar library, dev-server is pinnable but development-only, production is paid managed Oso Cloud. No surviving false unpinnable/latest-only claim except explicitly marked as corrected.
**Scope:** Docs-only confirmed; no code, NuGet pin, or container added.
**Test-coverage:** Docs-only — n/a; `harness lint` passed.

Review-log row (for the close-out PR body): `model: gpt-5.5` · `HEAD SHA: 2cc9f7ec832f227711e8958e2e6b84f7148d50f1` · `verdict: Go` · `evidence: PR #170`
