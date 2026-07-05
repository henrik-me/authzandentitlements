# CS26 — Expansion engines (SpiceDB/Cerbos/Keto/Oso/Topaz)

**Status:** done
**Owner:** yoga-ae-c4
**Branch:** cs26/content
**Started:** 2026-07-04
**Closed:** 2026-07-05
**Phase:** 7 — Expansion + Azure
**Lane:** Expansion
**Depends on:** CS05, CS15

## Goal

Broaden engine coverage by adding more adapters behind the same abstraction/playground.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 72b99c393582 | 2026-07-02T19:47:54Z | Go-with-amendments | Soften benchmark exit or add CS24; PDP and playground deps cover adapters but benchmark appearance otherwise races CS24. |

## Deliverables

- Adapters + AppHost containers for SpiceDB, Cerbos, Ory Keto, Oso, Topaz/Aserto.
- Each plugged into the scenario catalog + playground.
- Notes on the SpiceDB-vs-OpenFGA head-to-head.

## Exit criteria

- Expansion engines answer the scenario catalog and appear in the playground/benchmarks.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| SpiceDB: Authzed.Net CPM pin + csproj ref | done | sub-agent | agent-id=cs26-spicedb-impl \| role=engine-adapter \| report-status=complete \| learnings=2 — Authzed.Net 1.6.0 (gRPC) CPM pin + versionless csproj ref |
| SpiceDB: adapter (provider + pure mapper + lazy gRPC client + schema/relationship bootstrap) + DI registration | done | sub-agent | agent-id=cs26-spicedb-impl \| role=engine-adapter \| report-status=complete \| learnings=2 — provider/mapper mirror OpenFGA; lazy single-flight schema push + TOUCH seeding; fail-closed; reuses shared Rebac* graph so head-to-head is fair |
| SpiceDB: AppHost container (authzed/spicedb:v1.54.0, WithExplicitStart, no hard WaitFor) + endpoint injection | done | sub-agent | agent-id=cs26-spicedb-impl \| role=engine-adapter \| report-status=complete \| learnings=2 — opt-in container (h2c gRPC 50051), Pdp__SpiceDb__Endpoint+PresharedKey injected, no WaitFor |
| SpiceDB: tests (offline mapper + provider fail-closed; env-gated integration) | done | sub-agent | agent-id=cs26-spicedb-impl \| role=engine-adapter \| report-status=complete \| learnings=2 — PDP suite 759/759 green with NO container; integration soft-skips unless SPICEDB_TEST_ENDPOINT set |
| SpiceDB: playground entry + adapter doc + SpiceDB-vs-OpenFGA head-to-head | done | sub-agent | agent-id=cs26-spicedb-impl \| role=engine-adapter \| report-status=complete \| learnings=2 — playground opt-in + docs; GPT-5.5 R1 NO-GO (2 doc-fact blockers: schema snippet + consistency claim) → fixed → verified |
| Cerbos: Cerbos.Sdk CPM pin + csproj ref | done | sub-agent | agent-id=cs26-cerbos-impl \| role=engine-adapter \| report-status=complete \| learnings=3 — Cerbos.Sdk 1.10.2 (gRPC) CPM pin + versionless csproj ref |
| Cerbos: full-decision adapter (provider + gRPC client + YAML policies owning the fintech decision) + DI registration | done | sub-agent | agent-id=cs26-cerbos-impl \| role=engine-adapter \| report-status=complete \| learnings=3 — full-decision CEL policy (bank.yaml) owns the whole fintech decision; provider maps effect+output-token→AccessDecision, fail-closed; GPT-5.5 R1 found 2 fail-opens (delegation/OBO + malformed obligation) → fixed |
| Cerbos: AppHost container (ghcr.io/cerbos/cerbos:0.53.0, disk driver policy mount, WithExplicitStart) + endpoint injection | done | sub-agent | agent-id=cs26-cerbos-impl \| role=engine-adapter \| report-status=complete \| learnings=3 — opt-in container (h2c gRPC 3593), disk-driver policy bind-mount, Pdp__Cerbos__Endpoint injected, no WaitFor |
| Cerbos: tests (offline provider fail-closed + parity; env-gated integration) | done | sub-agent | agent-id=cs26-cerbos-impl \| role=engine-adapter \| report-status=complete \| learnings=3 — PDP suite 874/874 offline with NO container; 22-scenario parity validated against a real Cerbos container (integration 2/2) |
| Cerbos: playground entry + adapter doc | done | sub-agent | agent-id=cs26-cerbos-impl \| role=engine-adapter \| report-status=complete \| learnings=3 — playground opt-in + docs/authz/cerbos-adapter.md incl. the delegation/OBO/break-glass fail-closed boundary |
| Add Keto/Oso/Topaz | descoped | yoga-ae-c4 | Split into follow-ups (PR #144): Keto+Topaz → CS46; Oso → CS47 (de-scope — no pinnable in-process .NET path). CS26 closes on the 2 shipped engines (SpiceDB #134, Cerbos #139). |
| Wire into catalog + playground | done | yoga-ae-c4 | SpiceDB + Cerbos registered in AddPdp (PdpServiceCollectionExtensions.cs) so PlaygroundFanoutService auto-includes them (dynamic enumeration over all registered providers) + explicit opt-in entries in Playground.razor; both answer the scenario catalog (SpiceDB 759/759 offline; Cerbos 22-scenario Decision+reason parity vs a real container). |
| Close-out: docs + restart state | done | yoga-ae-c4 | CONTEXT.md updated with the CS26-complete narrative; WORKBOARD row removed at close-out; adapter/playground/head-to-head docs shipped in #134/#139. |
| Close-out: learnings + follow-ups | done | yoga-ae-c4 | Filed LRN-072..076; follow-ups CS45 (OBO guard) + CS46 (Keto+Topaz) + CS47 (Oso de-scope) filed & merged (PR #144). |

## Notes / Learnings

- **Shipped (2 of the 5 planned engines).** SpiceDB (ReBAC, PR #134) and Cerbos (out-of-process
  full-decision over gRPC, PR #139) landed behind the CS05 `IAuthorizationDecisionProvider` seam.
  Both are opt-in pinned containers (`authzed/spicedb:v1.54.0`, `ghcr.io/cerbos/cerbos:0.53.0`)
  wired with `WithExplicitStart` and **no hard `WaitFor`**, so the default `reference` path and the
  Docker-free build/test/`aspire run` loop stay unchanged.
- **Rescoped (the remaining 3).** Keto + Topaz → **CS46**; Oso → **CS47** (de-scope — no maintained
  pinnable in-process .NET/Polar path); the cross-cutting OBO/delegation/break-glass fail-closed
  guard surfaced by the Cerbos review → **CS45**. All three filed & merged (PR #144); CS26 closes on
  the two shipped engines.
- **Test posture.** SpiceDB PDP suite 759/759 offline (no container); Cerbos 874/874 offline plus a
  22-scenario `Decision`+reason parity run validated against a real `ghcr.io/cerbos/cerbos:0.53.0`
  container via the env-gated integration test.
- **Learnings filed (LRN-072..076):** LRN-072 (h2c cleartext-gRPC `Http2UnencryptedSupport` switch
  in an early static ctor), LRN-073 (lowercase gRPC metadata keys), LRN-074 (full-decision
  fail-closed obligation/output-token taxonomy), LRN-075 (cross-cutting OBO/delegation/break-glass
  fail-OPEN on engine swap → CS45), LRN-076 (full-decision policy parity is CI-invisible → env-gated
  pinned-container test).

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c4 |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck, independent sub-agent `cs26-pvi-review`) — independent of the claude-opus-4.8 implementers
**Date:** 2026-07-05T03:41:14Z
**Outcome:** GO

Reviewed the merged CS26 content (SpiceDB PR #134 + Cerbos PR #139) at branch HEAD `9e1c7b0964a3e5f55ef56bc11c4854d922146075` (R1) against the plan's Deliverables + Exit criteria. Blocking findings: **none**.

| Deliverable | Outcome | Rationale |
|---|---|---|
| D1 — adapters + AppHost containers for SpiceDB, Cerbos, Keto, Oso, Topaz | diverged | SpiceDB + Cerbos shipped (registered in `AddPdp`; pinned `WithExplicitStart` containers, no hard `WaitFor`; default `reference` unchanged). Keto/Topaz split to **CS46**, Oso to **CS47** (de-scope), the cross-cutting OBO/delegation guard to **CS45** — all filed & merged (PR #144); recorded, not silent. |
| D2 — plugged into scenario catalog + playground | diverged | Shipped engines are in the playground (`PlaygroundFanoutService` fans out over every registered provider + explicit `Playground.razor` entries) and answer the catalog (SpiceDB 759/759; Cerbos 22-scenario parity). Deferred engines carried by their follow-ups. |
| D3 — SpiceDB-vs-OpenFGA head-to-head note | match | `docs/eval/spicedb-vs-openfga.md` is a genuine head-to-head. |
| Exit criteria — answer catalog + appear in playground/benchmarks | diverged | Catalog + playground met for the shipped engines. **Benchmark/matrix appearance is incomplete** — SpiceDB/Cerbos are not in the benchmark `EngineCatalog.LiveEngineNames` (`["opa","openfga"]`) nor scored as integrated engines in `docs/eval/comparison-matrix.md`. This benchmark surface is owned by CS24 (done); the CS26 plan R1 review already **softened** this exit criterion (benchmark appearance "races CS24"). Recorded here as known future work for the benchmark surface, not a CS26 delivery gap. |

**Test-coverage:** sufficient for the shipped CS26 scope — offline provider/mapper/fail-closed tests plus env-gated real-container parity suites. Not covered (by accepted de-scope): Keto/Topaz/Oso (their follow-ups) and benchmark inclusion for SpiceDB/Cerbos (CS24-owned surface).

**Security:** verified the Cerbos adapter fails closed on OBO/delegation/break-glass (`CerbosDecisionProvider.cs:80-94`) and that the cross-cutting fail-open for the other non-reference engines is captured as CS45 (not silently ignored).

Review-log row (for the close-out PR body): `model: gpt-5.5` · `branch HEAD SHA: 9e1c7b0964a3e5f55ef56bc11c4854d922146075` · `R-round: R1` · `verdict: Go` · `evidence: PR #134 / PR #139`.
