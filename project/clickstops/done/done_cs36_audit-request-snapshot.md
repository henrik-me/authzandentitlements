# CS36 — Audit request-snapshot for faithful decision replay (LRN-057)

**Status:** done
**Owner:** yoga-ae-c3
**Branch:** cs36/content
**Started:** 2026-07-05
**Closed:** 2026-07-05
**Filed by:** yoga-ae-c3 — 2026-07-04, weekly LRN harvest (CS37): the one open learning (LRN-057) that needs a security-reviewed code change rather than a doc consolidation.
**Depends on:** CS13, CS15

## Goal

Give the CS15 Audit Explorer a **faithful 1:1 "replay"** of a recorded PDP decision by
persisting the full `AccessRequest` (subject + action + resource + context, incl. the ABAC
inputs `amount` / `maker` / `status` / roles / scopes / resource tenant+branch) alongside
each PDP-produced audit row, and using it to pre-fill the Playground exactly as the original
request — closing the LRN-057 fidelity gap where the CS13 tamper-evident row stores only
`subject id / action / resource type+id / single tenant / decision / reason / trace`, forcing
CS15 to ship a best-effort replay with an "uncaptured inputs" banner.

## Background

Per **LRN-057** (from CS15, PR #84): the audit row lacks the ABAC inputs needed to reproduce
a decision, so CS15's replay is honest-but-lossy (pre-fills the captured fields + a banner
naming the uncaptured ones). The learning flags the true fix as its own security-reviewed CS
because it changes the CS13 security-critical store.

Verified current architecture (this harvest):
- `AccessRequest` = `record AccessRequest(Subject, ActionRequest, Resource, EvaluationContext)`
  (`src/AuthzEntitlements.Authz.Pdp/Contracts/AccessRequest.cs`) — carries every ABAC input.
- The PDP emits `PdpDecisionAuditEvent`
  (`src/AuthzEntitlements.Authz.Pdp/Audit/PdpDecisionAuditEvent.cs`) — already richer than the
  wire contract (has DeterminingRule / PolicyReferences / Narrative / SubjectType / Actor*),
  but `IngestDecisionRequest`
  (`src/AuthzEntitlements.Audit.Service/Contracts/IngestDecisionRequest.cs`) forwards only 10
  scalar fields, so the request context is dropped at the wire.
- The store: `AuditEntry` (`src/AuthzEntitlements.Audit.Service/Data/AuditEntry.cs`) +
  `AuditPayload` + `AuditHashChain.ComputeRowHash`
  (`src/AuthzEntitlements.Audit.Service/Domain/AuditHashChain.cs`). The row hash binds a FIXED
  ordered field set; `ReceivedAtUtc` is the established precedent for a persisted-but-**not-hashed**
  server column ("observability metadata, not producer-supplied content").
- Replay UI: `src/AuthzEntitlements.Bank.Web` Playground + Audit Explorer (CS15) + the doc
  `docs/product/authz-playground-and-audit-explorer.md` § "Replay design — a deliberate
  fidelity trade-off".

Assumption (state it; verify at claim): the `audit` Postgres DB is dev-ephemeral (recreated per
`aspire run` from the CS13 migration; no durable production rows), so an additive nullable
column + a new migration carries no data-backfill burden.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Snapshot content + shape | Persist a **canonical JSON** serialization of the whole `AccessRequest` (Subject incl. type/roles/actor, Action, Resource incl. type/id/tenant/branch, Context incl. amount/maker/status/scopes) as one nullable string field `RequestSnapshot`, added additively to `PdpDecisionAuditEvent`, `IngestDecisionRequest`, and `AuditEntry`. Serialize ONCE in the PDP via a deterministic, fixed-property-order helper; store + return the exact string. | The full request is exactly what a faithful replay needs; a single canonical string is store-agnostic, deterministic, and reusable verbatim by the replay pre-fill without re-deriving fields. Additive positional defaults keep every existing construction compiling. |
| 2 | Tamper-evidence posture | Keep `RequestSnapshot` **non-hashed** (persisted like `ReceivedAtUtc`, excluded from `ComputeRowHash`); the tamper-evident chain continues to bind the authoritative decision fields (subject/action/resource/decision/reason/tenant/producer) unchanged. The Audit Explorer labels the replayed request as **reconstructed context, not part of the tamper-evident hash**. | Matches LRN-057's explicit "new non-hashed column", avoids a breaking `ComputeRowHash` format bump + chain-version migration for a convenience-replay feature, and preserves byte-identical verification of all existing/parallel-CS chains. Hashing-into-chain was considered and rejected (disproportionate blast radius; honest UI labeling covers the residual). |
| 3 | Fail-open-to-null, never fail-the-audit | If snapshot capture/serialization throws or the request is unavailable, ingest the audit row with `RequestSnapshot = null` (log-sanitized warning) — NEVER drop or fail the audit write. Rows without a snapshot (older rows, and non-PDP producers: gateway/bank audit middleware) replay via the existing CS15 best-effort banner path. | The audit-of-record must never be lost for a replay-convenience field; graceful degradation to the already-shipped best-effort replay. |
| 4 | Data minimization + security | Do NOT add any NEW data beyond what the PDP already processed for the decision; the snapshot is the same request already evaluated. Treat it as sensitive: it is only exposed through the existing access-controlled Audit Explorer/query surface, is CR/LF-log-sanitized if ever logged (LRN-059 convention), and the ingest server still stamps `Producer` (never client-trusted). No secrets/tokens are in `AccessRequest`; confirm during security review. **Bound the persisted snapshot** to a fixed maximum size (default ~16 KB, configurable); a serialization exceeding the cap persists `null` (fail-open per Decision #3, logged) rather than an unbounded blob — the ingest endpoint is intra-cluster and must never accept an unbounded persisted/queryable payload. | Keeps the change fail-closed, PII-neutral, and size-bounded (per GPT-5.5 plan review R1: prevent an unbounded persisted/returned string); a security-review sub-agent (distinct model) signs off before merge given the CS13 store is security-critical. |
| 5 | Replay UX | Audit Explorer "Replay in Playground" pre-fills faithfully from `RequestSnapshot` when present (drop the uncaptured-inputs banner for those rows, show the non-authoritative note instead); falls back to the CS15 best-effort pre-fill + banner when the snapshot is null. | Delivers the LRN-057 fidelity win where data exists, without regressing rows that predate it. |
| 6 | Scope guard | Touch only: `Authz.Pdp` (snapshot capture + event/contract field), `Audit.Service` (entity + migration + ingest/query/DTOs), `Bank.Web` (replay pre-fill + clients/DTOs), their test projects, and the CS15 replay doc. Do NOT alter `ComputeRowHash`'s hashed field set, the chain semantics, branch-protection, `.github/`, or CS21 files. | Minimal, security-contained blast radius; respects the multi-agent boundary (yoga-ae/CS21, yoga-ae-c5/CI). |

## Deliverables

- **PDP:** a deterministic canonical `AccessRequest` serializer (fixed property order; culture-invariant; stable null handling) + capture in `PdpDecisionService`; `PdpDecisionAuditEvent` and `IngestDecisionRequest` gain a nullable `RequestSnapshot` (additive, defaulted).
- **Audit.Service:** `AuditEntry.RequestSnapshot` (nullable) + a new EF Core migration (additive column, no hashed-field change); ingest endpoint persists it; `AuditPayload`/`ComputeRowHash` **unchanged** (snapshot excluded from the hash, like `ReceivedAtUtc`); query/response DTOs (`AuditResponses`/endpoints) return it.
- **Bank.Web Audit Explorer:** faithful replay pre-fill from `RequestSnapshot` when present (with the non-authoritative note), CS15 best-effort fallback when null; client DTOs updated.
- **Bounded-size guard:** a configurable maximum snapshot size (default ~16 KB); an over-limit canonical snapshot degrades to `null` (best-effort replay) rather than persisting an unbounded blob, with a test asserting the degradation.
- **Tests:** canonical-serializer determinism/round-trip; ingest+persist+query of the snapshot; hash-chain regression proving `RowHash` is unchanged by adding/altering the snapshot (tamper-evidence unaffected); fail-open-to-null on serializer failure AND on over-size; replay pre-fill (faithful vs best-effort) — minimum solid coverage per project, exact counts left to the implementer.
- **Docs:** `docs/product/authz-playground-and-audit-explorer.md` § replay updated to describe faithful replay when a snapshot is present + the non-authoritative labeling.
- **Gates:** independent security-review sub-agent sign-off (model distinct from implementers); full-solution `dotnet build` 0/0 (warnings-as-errors) + `dotnet test` green; `harness lint` green; `harness sync --mode=check` no drift.

## User-approval gates

- **Security posture confirmation (Decision #2):** the non-hashed-column choice is a security-relevant trade-off on the tamper-evident store. It is defensible + LRN-057-aligned + honestly UI-labeled, but if the close-out security review argues the snapshot must be hash-bound, escalate the chain-format-version decision to the maintainer rather than silently shipping either way.

## Exit criteria

- A PDP decision made through the Playground, then opened via the Audit Explorer "Replay in Playground", reproduces the original request faithfully from the persisted snapshot (no uncaptured-inputs banner for snapshot-bearing rows).
- Rows without a snapshot still replay via the CS15 best-effort path (no regression).
- The tamper-evident chain is provably unchanged (hash-chain tests green; `ComputeRowHash` hashed set untouched); snapshot serializer-failure fails open to a null snapshot, never dropping the audit row.
- Security review signs off; `dotnet build` 0/0 + `dotnet test` green; `harness lint` green; `harness sync --mode=check` no drift.

## Risks + open questions

- **Tamper-evidence of the snapshot** — non-hashed means an actor who can write the DB could alter the replayed request without breaking the chain; mitigated by honest UI labeling + the authoritative decision fields staying hashed. Re-confirm in security review (User-approval gate).
- **Serializer determinism** — replay pre-fill parses the stored JSON; keep the serializer/​deserializer symmetric and culture-invariant; cover with round-trip tests.
- **Ephemeral-DB assumption** — if the `audit` DB turns out to carry durable rows, the additive nullable column still needs no backfill (null replays best-effort); confirm at claim.
- **Sensitive-attribute exposure** — snapshot rides the existing access-controlled audit surface only; security review confirms no token/secret is present in `AccessRequest`.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched (orchestrator: yoga-ae-c3) | 2d9eb3a0a4d0 | 2026-07-04T22:52:00Z | Go-with-amendments | Fact-claims verified; non-hashed snapshot sound + LRN-057-aligned; fail-open-to-null OK. Amendment incorporated: bounded snapshot-size guard (default ~16 KB → null). |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| PDP: canonical `AccessRequest` serializer + capture in `PdpDecisionService`; add nullable `RequestSnapshot` to `PdpDecisionAuditEvent` + `IngestDecisionRequest` | done | yoga-ae-c3 | agent-id=cs36-impl / role=dotnet-feature / report-status=complete / learnings=0 |
| Audit.Service: `AuditEntry.RequestSnapshot` + additive EF migration; ingest persists it; `ComputeRowHash` UNCHANGED (snapshot excluded); query/response DTOs return it; bounded size guard (~16 KB → null, clamped to column width) | done | yoga-ae-c3 | agent-id=cs36-impl / role=dotnet-feature / report-status=complete / learnings=0 |
| Bank.Web Audit Explorer: faithful replay pre-fill from snapshot (via sequence-fetch, incl. OBO actor + distinct resource branch); CS15 best-effort fallback when null/malformed/fetch-failed; client DTOs | done | yoga-ae-c3 | agent-id=cs36-impl / role=dotnet-feature / report-status=complete / learnings=0 |
| Tests: serializer determinism/round-trip; ingest+persist+query; hash-chain-unchanged regression; fail-open-to-null (serializer + oversize); replay pre-fill (faithful/fallback, actor/branch, partial-actor, blank-scalar, cap clamp) | done | yoga-ae-c3 | agent-id=cs36-impl / role=dotnet-tests / report-status=complete / learnings=0 |
| Docs: update `docs/product/authz-playground-and-audit-explorer.md` replay section | done | yoga-ae-c3 | agent-id=cs36-impl / role=docs / report-status=complete / learnings=0 |
| Independent security review (distinct model) of the tamper-evident-store change | done | yoga-ae-c3 | GPT-5.5 security-review: **PASS** (non-hashed posture sound; no XSS/DoS; Producer server-stamped; no secrets in AccessRequest) |
| Close-out: docs + restart state | done | yoga-ae-c3 | CONTEXT.md updated; LRN-057 flipped applied |
| Close-out: learnings + follow-ups | done | yoga-ae-c3 | LRN-057 → applied on CS36 close |

## Notes / Learnings

- Implemented by an Opus-4.8 sub-agent (`cs36-impl`) + a fix sub-agent + orchestrator hand-fixes. Reviews: independent GPT-5.5 **security review PASS**; GPT-5.5 **review-of-record** R1/R2 NEEDS-FIX (replay not 1:1 for actor/branch; silent >2000-char drop; a configurable-cap-vs-DB-column-width fail-open gap; a fetch-failure banner mislabel) → **R3 GO**; then Copilot caught a partial-actor bug + a blank-required-scalar false-faithful path (both fixed → GPT-5.5 GO), plus two non-blocking robustness nits resolved-with-rationale (LRN-020 convergence).
- Merged as PR #140 (`8fe3911`) after rebasing onto latest `origin/main` (additive CS21 break-glass merge in `PdpDecisionAuditEvent`/`PdpDecisionService`; whole-solution build+test re-run green — LRN-040). `dotnet build` 0/0, `dotnet test` all pass, `harness lint` 0, tamper-evident hash files byte-identical.
- No new repo learnings warranted (the sub-agent's candidates — `dotnet ef` BOM/CRLF, no-DB-provider test posture, additive-optional-param for shared services — are process-local; the additive-optional-param one is already covered by the LRN-058/CS19 additive-defaulted-member convention).

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c3 |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-05T01:58:00Z
**Outcome:** GO

Per-deliverable outcome:

| Deliverable | Outcome | Rationale |
|---|---|---|
| PDP canonical serializer + capture + additive `RequestSnapshot` on event/contract | match | `RequestSnapshotSerializer` deterministic/fail-open; `PdpDecisionService` captures + logs null; contracts gained a nullable defaulted field. |
| Audit.Service: column + additive migration; bounded ingest; writer independent of hash; hash files unchanged; query returns it | match | Nullable `varchar(16384)` migration/model; `RequestSnapshotOptions` clamps the cap to the column width; `AuditChainWriter` attaches the snapshot after the row hash; `AuditHashChain.cs`/`AuditPayload.cs` diff empty. |
| Bank.Web faithful replay (actor + resource branch) via sequence-fetch; best-effort fallback; non-authoritative labeling | match | Replay link passes the sequence; Playground fetches the full row, parses the snapshot to the full form model (incl. actor/resource branch), falls back to CS15 scalar pre-fill, labels the snapshot non-hashed. |
| Bounded size guard + fail-open-to-null logging | match | Serializer failure → null + sanitized warning; oversize → null + sanitized warning. |
| Tests (serializer/ingest/hash-unchanged/fail-open/replay/actor/branch/cap/partial-actor/blank-scalar) | match | Coverage across PDP, Audit.Service, Bank.Web; hash-independence + replay edge cases specifically tested. |
| Docs updated | match | Replay doc rewritten for faithful/best-effort, non-hashed posture, size guard, fail-open. |
| Gates/security/build/test/lint | match | Security PASS, GPT-5.5 GO, build 0/0, tests pass, `harness lint` 0; diff review found no contradiction. |

**Test coverage:** sufficient. Audit.Service has no DB-provider integration (repo's pure-domain posture); the ingest→persist→query path is covered compositionally (guard/options, `AuditChainWriter` row materialization + hash-independence, `AuditEndpoints.ToEntryView` projection) — adequate for a simple additive nullable scalar EF mapping.

**Outcome GO:** All 7 deliverables match; tamper-evidence provably intact; no mis-scope or inaccurate claim. Confirms the pre-merge security PASS + multi-round GPT-5.5 review-of-record.
