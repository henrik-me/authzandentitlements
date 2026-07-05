# CS47 — Oso disposition: de-scope from the expansion-engine set

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
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
  - **The only .NET path is Oso Cloud:** the `OsoCloud` NuGet SDK (`1.10.0`, net6.0→net10, HTTP REST) talks to the hosted Oso Cloud API **or** a local **dev-server** image `public.ecr.aws/osohq/dev-server:latest`.
  - **The dev-server is `latest`-only** (no pinned semver tag confirmed from public docs) and is **explicitly a development server** (no SLA/persistence/production support). Production requires a **paid Oso Cloud** account.
- This conflicts with two established repo postures: the **image-pinning-for-determinism** convention (opt-in engines pin their image tags; `latest` is non-deterministic) and the **self-host-first-with-managed-optionality** decision (**ADR-0007**, from CS25) — Oso offers no pinnable self-hostable production path.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Implement vs. de-scope | **De-scope** — do not ship an Oso adapter | No maintained/publicly-discoverable in-process .NET library (as of 2026-07-04); the only local path is an unpinnable (`latest`-only), dev-only dev-server with a paid-cloud production requirement — incompatible with the repo's image-pinning determinism convention and the ADR-0007 self-host-first posture. |
| 2 | Capture the decision durably | Record it in the **eval docs** (extend `docs/eval/comparison-matrix.md`, `docs/eval/market-survey.md`, AND the detailed survey page `docs/eval/survey/policy-and-decision-engines.md` — superseding its older "local/self-hosted PDP binary" wording) and add a short **ADR** (plus a `docs/adr/README.md` index entry) documenting the de-scope + rationale | Knowledge lives in the repo (not agent memory); updating the detailed survey page too prevents the docs from contradicting the ADR; the index entry makes the decision discoverable + cross-linked from the eval + expansion narrative. |
| 3 | Re-evaluation trigger | Revisit **only if** Oso ships either (a) a maintained in-process .NET/Polar library targeting modern .NET, or (b) a **pinnable, self-hostable** production server image (not `latest`-only, not paid-cloud-only) | Keeps the door open without committing to a moving/managed-only target; ties re-entry to the exact constraints that block it today. |
| 4 | Scope of this CS | **Docs only** — no adapter code, no NuGet pin, no container | The deliverable is a justified disposition + durable record, not an implementation. |

## Deliverables

- An **ADR** (`docs/adr/0008-oso-descoped-from-expansion-engines.md`) recording the de-scope decision, the evidence (no maintained/publicly-discoverable in-process .NET/Polar library as of 2026-07-04; `latest`-only dev-only dev-server; paid-cloud production), the conflicting repo postures (image-pinning determinism; ADR-0007 self-host-first), and the re-evaluation trigger (Decision #3); plus a `docs/adr/README.md` index entry for discoverability.
- Eval-doc updates: an Oso row/entry in `docs/eval/comparison-matrix.md` and `docs/eval/market-survey.md`, **and** an update to the detailed survey page `docs/eval/survey/policy-and-decision-engines.md` (superseding its older "local/self-hosted PDP binary" wording), each marking Oso **evaluated → de-scoped** with a one-line reason + a link to the ADR.
- The expansion-engine narrative (CS26 close-out notes and/or `docs/authz/adding-an-engine-adapter.md` engine list) updated so the shipped/planned expansion set reads **Cerbos + SpiceDB + Keto + Topaz**, with **Oso intentionally excluded** (linked to the ADR) — never as a silent gap.
- A dated honesty caveat + sources on the pricing/availability claims (per the CS25/LRN-064 eval-doc convention).

## User-approval gates

- None. This is a documentation/decision CS. (If a future maintainer wants Oso implemented despite the constraints — e.g. accepting the `latest` dev-server for lab-only demo — that reopens Decision #1 and is a fresh CS.)

## Exit criteria

- Oso is documented as **intentionally de-scoped** with an evidence-backed rationale, an ADR, eval-doc entries, and an explicit re-evaluation trigger; the expansion-engine set is stated as Cerbos/SpiceDB/Keto/Topaz with Oso excluded; `harness lint` green (text-encoding, xref durability). No adapter code, NuGet pin, or container is added.

## Risks + open questions

- **Staleness.** Oso's product/packaging could change; the dated caveat + the re-evaluation trigger (Decision #3) mitigate this.
- **Cross-link durability.** Durable docs must not link to `active/` clickstops; reference the ADR + the `done/` CS26 record or SHA permalinks (harness lint enforces).
- **Minimal risk otherwise** — docs-only, no runtime surface.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | cs45-47-plan-review (rubber-duck) | 2e06e467a8dc | 2026-07-05T02:48:12Z | Go-with-amendments | Added detailed survey-page + ADR-index updates to deliverables; softened the strongest historical claim to "as of 2026-07-04" — applied. Rationale fits ADR-0007 + image-pinning. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| (populated at claim time per § Claim) | planned | — | — |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
