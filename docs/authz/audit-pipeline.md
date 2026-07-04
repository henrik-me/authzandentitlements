# Tamper-evident audit log pipeline

> **Scope:** how every authorization decision becomes a **tamper-evident, append-only,
> hash-chained** audit row. It documents the shipped
> [`AuthzEntitlements.Audit.Service`](../../src/AuthzEntitlements.Audit.Service) — the hash-chain
> design, the ingest/verify/query API — and the **producer seam** in
> [`AuthzEntitlements.Authz.Pdp`](../../src/AuthzEntitlements.Authz.Pdp) that forwards PDP
> decisions to it. Read the [PDP contract](pdp-contract.md) first: the audit pipeline consumes the
> per-decision events the PDP already emits. See also [ARCHITECTURE.md](../../ARCHITECTURE.md) and
> the [observability stack](../../src/AuthzEntitlements.AppHost/AppHost.cs) (CS12).

## What it is (and why)

Regulated fintech workloads must be able to prove, after the fact, **exactly which authorization
decisions were made and that the record was not altered**. A plain log table is not enough: anyone
with database write access could edit or delete a row and erase the evidence. The audit pipeline
makes that class of tampering **detectable**.

Every decision is stored as a row in an **append-only hash chain**: each row carries the hash of
the previous row plus a hash of its own content, so the rows form a linked chain (the same
construction a blockchain uses for its block headers). Changing, reordering, inserting, or deleting
any row breaks the chain, and the [`/api/audit/verify`](#get-apiauditverify) endpoint recomputes
the whole chain to report the first break. The chain does not *prevent* tampering — it makes
tampering **evident**.

```
   PDP decision                Audit.Service                    Postgres `audit` DB
  (once per eval)          (single-writer append)             (append-only hash chain)
 ┌───────────────┐        ┌──────────────────────┐           ┌────────────────────────┐
 │ PdpDecision   │  HTTP  │ POST /api/audit/      │  EF Core  │ seq  prevHash  rowHash │
 │ AuditEvent    │───────▶│      decisions        │──────────▶│  1   000..00   a1b2..  │
 │ (subject,     │  JSON  │  AuditChainWriter:    │  (Npgsql) │  2   a1b2..    c3d4..  │
 │  action,      │        │  read tail → compute  │           │  3   c3d4..    e5f6..  │
 │  decision,    │        │  rowHash → append     │           │        …               │
 │  reason, …)   │        └──────────────────────┘           └────────────────────────┘
 └───────────────┘                    ▲                                   │
        │ IPdpDecisionAuditSink       │ GET /api/audit/verify  ◀──────────┘  recompute chain
        │ (non-blocking channel       │ GET /api/audit/entries ◀──────────┘  filtered read model
        │  + background forwarder)    │
```

## The hash chain

The chain logic lives in the pure, DB-free
[`AuditHashChain`](../../src/AuthzEntitlements.Audit.Service/Domain/AuditHashChain.cs) so it can be
unit-tested by folding an in-memory chain with no Postgres.

- **Genesis.** The first entry's `PrevHash` is the sentinel `GenesisPrevHash` — 64 lowercase `0`
  hex chars (SHA-256 width), a value no real digest collides with in practice.
- **Row hash.** `RowHash = SHA-256(canonical)` rendered as lowercase hex, where `canonical` is a
  fixed-field-order JSON object written with `Utf8JsonWriter` binding **the sequence, the previous
  row's hash, and every persisted content field**: `timestampUtc` (ISO-8601 round-trip `"O"`),
  `traceId`, `provider`, `subjectId`, `action`, `resourceType`, `resourceId`, `decision`,
  `reason`, `tenant`, `producer`. Because the fields are length-delimited and quoted/escaped, no
  field boundary can be forged by shifting content between fields, and a `null` string is written
  as JSON `null` — distinct from `""`.
- **Linkage.** Entry *N*'s `PrevHash` equals entry *N-1*'s `RowHash`, and sequences are contiguous
  starting at 1.

`RowHash` binds the sequence and the previous hash, so altering **any** field, reordering rows,
deleting a row (a sequence gap), or re-linking the chain all cause a recomputed hash to diverge.

### What verification catches

[`AuditHashChain.Verify`](../../src/AuthzEntitlements.Audit.Service/Domain/AuditHashChain.cs) walks
the chain in ascending sequence order and returns the **first** break (`BrokenAtSequence` + a
human-readable reason):

| Tamper | How it is caught |
|---|---|
| Edited a field (e.g. flipped `Deny`→`Permit`) | recomputed `RowHash` ≠ stored `RowHash` |
| Overwrote a stored `RowHash` | prev-hash link of the next row no longer matches |
| Deleted a row | sequence gap (non-contiguous) |
| Reordered rows | sequence mismatch / prev-hash link break |
| First row's `PrevHash` altered | ≠ `GenesisPrevHash` |

An empty chain is trivially valid.

## The Audit.Service API

A minimal-API ASP.NET Core service that owns the `audit` Postgres database (EF Core + Npgsql, same
stack as the [Entitlements service](../../src/AuthzEntitlements.Entitlements.Service)). It applies
its `InitialCreate` migration on startup. Endpoints are anonymous — the service is called
intra-cluster by decision producers; edge/token concerns are handled in other CSs.

### `POST /api/audit/decisions`

Ingest one decision and append it to the chain. Body
([`IngestDecisionRequest`](../../src/AuthzEntitlements.Audit.Service/Contracts/IngestDecisionRequest.cs),
camelCase) mirrors the PDP's `PdpDecisionAuditEvent`; the server stamps `Producer = "pdp"` so a
caller cannot spoof the recorded producer identity.

```jsonc
// request
{ "timestampUtc": "2026-07-04T02:00:00.000+00:00", "traceId": "0af7…", "provider": "reference",
  "subjectId": "user:alice", "action": "account.transfer", "resourceType": "account",
  "resourceId": "acct-123", "decision": "Permit", "reason": "PermitOwnerAccess", "tenant": "acme" }
// 201 Created
{ "sequence": 42, "prevHash": "…", "rowHash": "…" }
```

Appends are serialized by
[`AuditChainWriter`](../../src/AuthzEntitlements.Audit.Service/Services/AuditChainWriter.cs): a
process-wide `SemaphoreSlim(1,1)` held across *read-tail → compute-next → insert → commit* makes a
single logical writer, so two concurrent requests cannot compute the same sequence and fork the
chain.

### `GET /api/audit/verify`

Recompute the entire chain and report tamper status: `{ "valid": true, "entryCount": 42,
"brokenAtSequence": null, "reason": null }`.

### `GET /api/audit/entries`

Filtered, paged read model. Optional query params: `subject`, `action`, `decision`, `tenant`,
`trace`, `producer`, `limit` (default 100, max 500), `offset`. Returns entries in sequence order
including their `prevHash`/`rowHash`.

## Producer ingestion — the PDP sink

The PDP already emits exactly one
[`PdpDecisionAuditEvent`](../../src/AuthzEntitlements.Authz.Pdp/Audit/PdpDecisionAuditEvent.cs) per
evaluation through the
[`IPdpDecisionAuditSink`](../../src/AuthzEntitlements.Authz.Pdp/Audit/IPdpDecisionAuditSink.cs)
seam (CS05). CS13 adds a second implementation that forwards those events to the Audit.Service —
**without ever coupling the decision hot path to the audit store**:

- [`HttpForwardingPdpDecisionAuditSink`](../../src/AuthzEntitlements.Authz.Pdp/Audit/HttpForwardingPdpDecisionAuditSink.cs)
  `Record()` runs on the decision path, so it only does a **non-blocking** `TryWrite` onto a bounded
  channel — it never blocks and never throws. If the channel is full it **drops and counts** the
  event (availability over completeness) rather than stalling authorization.
- [`AuditForwardingWorker`](../../src/AuthzEntitlements.Authz.Pdp/Audit/AuditForwardingWorker.cs) (a
  `BackgroundService`) drains the channel and POSTs each event to
  `{Audit:ServiceUrl}/api/audit/decisions`. A non-2xx response or a transport exception is logged
  and swallowed so a transient or absent Audit.Service never crashes the PDP.

### Config gating (the default stays offline)

[`PdpAuditSinkServiceCollectionExtensions.AddPdpDecisionAuditSink`](../../src/AuthzEntitlements.Authz.Pdp/Audit/PdpAuditSinkServiceCollectionExtensions.cs)
selects the sink from the `Audit` config section, mirroring the config-gated posture of the OPA /
OpenFGA / Unleash integrations:

| `Audit:Sink` | Behaviour |
|---|---|
| unset / `logging` (**default**) | the CS05 `LoggingPdpDecisionAuditSink` — offline, deterministic; no channel, no worker, no network. Every existing PDP test keeps this default. |
| `http` | the forwarding sink + background worker. Requires a non-blank `Audit:ServiceUrl`; **fails closed** (`InvalidOperationException` at startup) if it is missing. |

Other keys: `Audit:ChannelCapacity` (default 2048), `Audit:HttpTimeoutSeconds` (default 10).

## Aspire wiring

[`AppHost.cs`](../../src/AuthzEntitlements.AppHost/AppHost.cs) provisions the `audit` database on
the shared Postgres server and runs `audit-service` as a **first-class part of the default
`aspire run` stack** (Postgres already runs for entitlements, so — unlike the opt-in engines — the
audit service is not `.WithExplicitStart()`). It then turns on the PDP forwarder by injecting
`Audit__Sink=http` and `Audit__ServiceUrl` (the audit-service `http` endpoint) into `authz-pdp`.
There is deliberately **no** hard `WaitFor(auditService)` on the PDP: the forwarder buffers and is
resilient, so the decision path never blocks on the audit store being up.

Because the PDP default is the logging sink, `dotnet build` / `dotnet test` and any run without the
`Audit__Sink=http` injection stay fully deterministic and offline — the HTTP forwarder only engages
under the AppHost-wired full stack.

## Limitations & future work

- **Single-writer / single-instance.** The in-process `SemaphoreSlim` only serializes writers
  within one service instance; the `Sequence` primary key + unique `RowHash` index are the DB-level
  backstop that makes a second concurrent instance fail loudly rather than silently fork the chain.
  Scaling the writer out would need a DB advisory lock or an append-only sequence generator.
- **Detection, not prevention.** The chain makes tampering evident on `verify`; it does not stop a
  privileged actor from rewriting the whole chain. External anchoring (periodically publishing the
  latest `RowHash` to an independent store) would close that gap and is out of scope for CS13.
- **Producers.** PDP decisions are wired today. Entitlement / JIT / approval producers wire into the
  same `POST /api/audit/decisions` contract (with their own `producer` value) as CS10 / CS11 land.
