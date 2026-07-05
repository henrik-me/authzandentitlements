# CS54 — Codify out-of-process PDP adapter safety conventions

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Filed by:** yoga-ae-c2 on 2026-07-05 — harvest of open learnings LRN-072/073/074/076 (all `source_cs: CS26`, `claim_area: pdp-adapters`), surfaced while dispositioning the open-learnings backlog. State-of-world probe (2026-07-05): the on-disk max CS id was 50 and `docs/file-planned-cs51-*` was in flight, so this was originally filed as CS52; **renumbered to CS54** on 2026-07-05 after a concurrent sibling (yoga-ae-c3) merged a different CS52 (`product-eval-refactor-and-coverage`) to `main`.
**Depends on:** none

## Goal

Consolidate the four recurring **out-of-process / full-decision PDP engine-adapter safety patterns** surfaced by LRN-072/073/074/076 into canonical, discoverable conventions — the adapter-author contract (`docs/authz/pdp-contract.md`) plus a pointer in the `CONVENTIONS.md` project-local block — so every future engine adapter inherits them from one document instead of re-deriving them from prior adapters and PR review logs.

## Background

- CS26 (SpiceDB + Cerbos in-process adapters) surfaced four durable adapter-authoring invariants that today live **only** in code plus `LEARNINGS.md`, not as canonical conventions:
  - **LRN-072 (tooling):** .NET's `SocketsHttpHandler` refuses HTTP/2-over-cleartext (h2c) by default; the `System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport` `AppContext` switch must be set in a **static constructor that runs before any handler / `GrpcChannel` is constructed** (the runtime caches the flag on first handler construction, so late-setting is silently inert). Pair with fail-closed rejection of `https://` endpoints (the h2c path is cleartext-only) and `Uri.TryCreate` absolute-URI + scheme validation.
  - **LRN-073 (tooling):** gRPC metadata / `CallCredentials` keys **must be lowercase** (`authorization`, never `Authorization`); a mis-cased key is rejected by the gRPC stack, so a correctly-configured credential silently fails to authenticate.
  - **LRN-074 (architectural):** a **full-decision** adapter (the engine owns the whole fintech decision) must **fail closed on every response-mapping ambiguity** — an unknown/typo obligation token → deny (never drop the obligation); a known action returning no output row → `ProviderUnavailable` (not `UnknownAction`); multiple/ambiguous output rows → deny (never arbitrarily pick one).
  - **LRN-076 (process):** an out-of-process (full-decision or ReBAC) adapter needs an **env-gated integration test** against a real **pinned** container (`<ENGINE>_TEST_ENDPOINT`) that soft-skips when the variable is unset — the offline suite and CI stay Docker-free/green while a documented local run validates the CI-invisible live wire/policy surface. A green offline suite is necessary-but-insufficient.
- Each learning's "implications carried forward" explicitly targets the **next** engine adapter (CS46 Keto/Topaz), i.e. these are cross-adapter invariants, not one-off fixes. Codifying them upstream turns four open learnings into a single canonical reference and closes them.
- Existing surfaces this CS builds on: `docs/authz/pdp-contract.md` (the adapter-author contract), the `CONVENTIONS.md` `conventions.project` local block, and the shipped adapters under `src/AuthzEntitlements.Authz.Pdp/Providers/` (SpiceDb, Adapters/Cerbos) that already implement the patterns as worked examples.
- Scope note: CS46 (Keto/Topaz) is the first consumer of these conventions and is in flight under a different owner; this CS is **documentation-only** and touches disjoint files (docs + the CONVENTIONS local block), so it does not race CS46's adapter code.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Primary home for the conventions | A new "Out-of-process engine adapter safety" section in `docs/authz/pdp-contract.md`, cross-linked from a one-line entry in the `CONVENTIONS.md` `conventions.project` local block | `pdp-contract.md` is already the adapter-author contract (domain detail belongs there); `CONVENTIONS.md` is the discoverable index. Avoids duplicating domain detail into the conventions file. |
| 2 | h2c switch guidance (LRN-072) | Document: set the `Http2UnencryptedSupport` `AppContext` switch in a static ctor **before** any handler/channel is built; reject `https://` fail-closed; validate absolute URI + scheme | Placement is load-bearing (the runtime caches the flag on first handler construction); fail-closed misconfiguration handling is the durable rule. Cite the SpiceDb/Cerbos check services as worked examples (by concept, not line number). |
| 3 | gRPC metadata casing (LRN-073) | Document: all gRPC metadata / `CallCredentials` keys MUST be lowercase; keep an **offline casing regression test** as the enforcing pattern | A mis-cased key is silently rejected by the gRPC stack; an offline test prevents silent re-breakage of live auth without needing a container. |
| 4 | Full-decision fail-closed mapping (LRN-074) | Document a **fail-closed response-mapping checklist**: unknown obligation token → deny; known-action-with-no-output → `ProviderUnavailable`; ambiguous/multi-row output → deny; enumerate every unknown/empty/ambiguous engine output | Fintech authorization must never fail open; a checklist makes the enumeration explicit and testable for every future full-decision engine. |
| 5 | Env-gated integration-test convention (LRN-076) | Document: every out-of-process adapter carries an env-gated (`<ENGINE>_TEST_ENDPOINT`) integration test that **soft-skips** when unset, validating live parity against a **pinned** container image — for a **full-decision** adapter, `Decision` + primary reason-code parity; for a **ReBAC** adapter, live schema/seed/relationship-check semantics (PDP reason mapping stays covered by the offline suite) | Keeps the offline suite and CI Docker-free and green while a documented local run validates the CI-invisible policy/wire surface; treat a green offline suite as necessary-but-insufficient. |
| 6 | Scope boundary | Documentation-only consolidation; **no adapter code changes** in this CS | The shipped SpiceDb/Cerbos adapters already implement these patterns (CS26); this CS makes them canonical and discoverable. Any code drift found is a separate CS, not folded in here. |
| 7 | Citation style | Cite adapter files by **file + concept** (worked examples), never by line number | Adapter line numbers drift across edits/syncs; concept-anchored citations stay valid (mirrors the F2 plan-review discipline). |

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched (cs52-plan-review) | 179f2e6bf885 | 2026-07-05T17:22:00Z | Go-with-amendments | Decision #5 over-generalized reason-parity to ReBAC adapters; split full-decision (Decision+reason) vs ReBAC (schema/relationship) test parity. |
| R2 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched (cs52-plan-review-r2) | 2b4533cf305e | 2026-07-05T17:33:00Z | Go | R1 resolved: Decision #5 now separates full-decision Decision+reason parity from ReBAC schema/seed/check semantics; no new inconsistency. |
| R3 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched (cs54-plan-review-r3) | 971c73de7040 | 2026-07-05T19:03:00Z | Go | Renumber CS52→CS54 (concurrent sibling CS52 collision); Decisions byte-identical, Deliverables only decouple the CS-number ref; body consistent. |

## Deliverables

- `docs/authz/pdp-contract.md` gains an **"Out-of-process engine adapter safety"** section documenting all four patterns — (a) the h2c `AppContext` switch placement + `https://` fail-closed rejection + URI/scheme validation, (b) lowercase gRPC metadata / `CallCredentials` keys + an offline casing regression test, (c) the full-decision fail-closed response-mapping checklist (unknown obligation → deny; no-output → `ProviderUnavailable`; ambiguous → deny), and (d) the env-gated `<ENGINE>_TEST_ENDPOINT` soft-skip integration-test convention against a pinned container — each with a worked-example citation to the shipped SpiceDb/Cerbos adapter (by concept, not line number).
- `CONVENTIONS.md` `conventions.project` local block gains a short **"PDP engine adapters"** entry pointing at the new `pdp-contract.md` section (discoverable index; edited only between the `harness:local-start id=conventions.project` / `harness:local-end` markers).
- `LEARNINGS.md`: LRN-072, LRN-073, LRN-074, and LRN-076 flip to `status: applied` at this CS's close-out, each `**Disposition:**` citing this CS and the merge commit. (They stay `open`, linked to this CS, until then.)
- No code changes; the full solution stays green (`dotnet build` + `dotnet test`), and `harness lint` passes (including the text-encoding gate — LF/no-BOM).

## User-approval gates

- None — documentation-only; no infrastructure and no billable resources are provisioned.

## Exit criteria

- `docs/authz/pdp-contract.md` documents all four out-of-process adapter safety patterns, each with a concept-anchored worked-example citation; `CONVENTIONS.md` `conventions.project` points at it.
- A future adapter author can follow the doc without reading prior adapter PRs or `LEARNINGS.md`.
- `harness lint` passes; the full `dotnet build` + `dotnet test` stay green.
- LRN-072/073/074/076 are dispositioned `applied` at close-out with a commit citation.

## Risks + open questions

- **Doc drift.** Conventions must cite stable anchors (file + concept), not line numbers, since adapter line numbers drift across edits/syncs. Mitigated by Decision #7.
- **Overlap with active CS46 (Keto/Topaz).** CS46 is the first consumer of these conventions and is in flight under another owner; this CS is documentation-only on disjoint files (docs + the CONVENTIONS local block), so there is no file-ownership race. It documents the pattern CS46 follows.
- **Home choice.** Whether the primary home should be `pdp-contract.md` or `ARCHITECTURE.md`; chose `pdp-contract.md` (the adapter-author contract) with a `CONVENTIONS.md` pointer, per Decision #1.
- **Composed-file discipline.** `CONVENTIONS.md` is a composed file; edits must stay strictly within the `conventions.project` local-block markers or `harness sync` will overwrite them.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| (populated at claim time per § Claim) | planned | — | — |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
