# AuthZ Playground & Audit Explorer (CS15)

Two developer-facing surfaces in `AuthzEntitlements.Bank.Web` that make the
fine-grained authorization layer inspectable:

- **AuthZ Playground** (`/playground`) — author one authorization request and
  fan it out across every decision engine at once, comparing each engine's
  verdict side by side.
- **Audit Explorer** (`/audit`) — filter and search the tamper-evident decision
  log, confirm the hash chain is intact, and replay any entry.

Both pages are Blazor **Interactive Server** components guarded by
`[Authorize]`, so a signed-in user drives them live over the circuit. Neither
forwards the user's token: the PDP and the audit service are anonymous in this
lab (called intra-cluster), so the playground authors the subject freely rather
than binding it from the token identity.

## How they relate to the four-layer authz model

The [Bank.Web product tour](bank-web.md) walks one workflow through four
authorization layers (AuthN → coarse gateway → fine authorization →
entitlements). These two surfaces zoom in on the **fine authorization** layer:
the Playground previews how the pluggable PDP engines decide a single request,
and the Audit Explorer inspects the tamper-evident record those real
PDP-mediated decisions leave behind.

## AuthZ Playground (`/playground`)

Build one fine-grained `AccessRequest` — subject (type, id, roles, tenant,
branch), action, resource (type, id, tenant, amount, maker, status), and
context scopes — or apply one of the built-in **presets**:

- *Permit: teller reads own-tenant account*
- *Deny: teller reads other-tenant account* (subject in `CONTOSO`, resource in
  `FABRIKAM` — every engine returns `TenantMismatch`)
- *Threshold: teller creates a $15,000 transfer*

Setting **Resource tenant** different from the subject **Tenant** models a
cross-tenant request, so the presets and the form can both demonstrate a
`TenantMismatch` deny.

Select which engines to fan out to. The in-process engines — `reference`,
`aspnet`, `casbin`, `cedar` — are **checked by default**. The container-backed
engines — `opa` and `openfga` — are **opt-in** (unchecked, with a "needs its
container running" hint), because they only answer when their explicit-start
container is up; otherwise they fail closed and render an **Unavailable** row.

Running the fan-out compares each engine's
`{decision, reason(s), explanation, latency, trace}` in a table, with a
top-level **"All engines agree / Engines disagree"** call-out computed over the
*available* engines only (an unreachable engine's fail-closed Deny is never
mistaken for a genuine disagreement).

### What-if semantics — writes NO audit entry

The Playground is a **what-if surface**. The fan-out endpoint resolves engines
by name and evaluates the providers directly — it never runs the enforcement
path, so it emits **no audit event** and no decision metric. It mirrors the
CS17 what-if / shadow tooling: a preview of how the engines compare on the same
input, not an enforced decision. The Audit Explorer is therefore *not* populated
by playground activity.

### Trace link is best-effort

Each engine result carries the ambient trace id if one is flowing. It renders as
a **deep link** to the observability stack (Grafana/Tempo, CS12) only when
`Observability:BaseUrl` is configured; otherwise it is shown as plain text. When
nothing is sampling, the trace id may be absent (rendered as `—`). The
playground never starts its own span — it only surfaces the trace context
already in flight.

### Backend contract — `POST /api/authz/playground/fanout`

The page calls the PDP's fan-out endpoint through `IPdpClient.FanoutAsync`. A
null or empty `engines` list fans out across every registered provider. The wire
shape is camelCase with string enum values:

```json
{
  "request": {
    "subject": {
      "type": "user",
      "id": "user-teller1",
      "roles": ["Teller"],
      "tenant": "CONTOSO",
      "branch": null
    },
    "action": { "name": "bank.account.read" },
    "resource": {
      "type": "account",
      "id": null,
      "tenant": "CONTOSO",
      "branch": null,
      "amount": null,
      "makerId": null,
      "status": null
    },
    "context": { "scopes": ["bank.read"] }
  },
  "engines": ["reference", "aspnet", "casbin", "cedar"]
}
```

The response returns every engine's result, the best-effort top-level trace id,
and the `allAgree` verdict computed server-side over the available engines:

```json
{
  "results": [
    {
      "engine": "reference",
      "decision": "Permit",
      "reasons": [{ "code": "Permit", "message": "..." }],
      "obligations": [],
      "explanation": {
        "engine": "reference",
        "determiningRule": "...",
        "policyReferences": [{ "kind": "...", "reference": "...", "detail": null }],
        "narrative": "..."
      },
      "latencyMs": 0.42,
      "traceId": "abc123...",
      "available": true,
      "unavailableReason": null
    }
  ],
  "traceId": "abc123...",
  "allAgree": true
}
```

An unreachable engine returns `available: false` with the failure in
`unavailableReason` and is excluded from `allAgree`. Malformed bodies and unknown
engine names fail closed with a `400` (never a `500` or a wrong-engine result).

## Audit Explorer (`/audit`)

Search the tamper-evident decision log and confirm its integrity.

- **Filter / search** — `GET /api/audit/entries` supports the
  `sequence`, `subject`, `action`, `decision`, `tenant`, `trace`, `producer`,
  `limit`, and `offset` filters (AND semantics; blanks omitted). The new
  `sequence` filter resolves a single row by its chain position.
- **Chain verification badge** — on load and on demand the page calls
  `GET /api/audit/verify`, which recomputes the whole hash chain and renders a
  green **Chain valid** badge (with the entry count) or a red **Chain BROKEN at
  #\<seq\>** badge with the reason. `/verify` optionally accepts a trusted
  checkpoint (`expectedSequence` + `expectedRowHash`); a partial or malformed
  checkpoint fails closed with a `400` rather than a silent bare verify.
- **Replay** — each row links to *"Replay in Playground"* (see below).

## Replay design — a deliberate fidelity trade-off

Replay is **"open in Playground"**, not a one-click re-run: the Audit Explorer
pre-fills the Playground form and shows the **recorded decision** alongside the
live cross-engine fan-out for comparison, but the user runs the fan-out. The
"Replay in Playground" link carries the entry's **sequence** (not the snapshot
itself); the Playground fetches the full audit entry — including its request
snapshot — by that sequence from the audit query API, so faithful replay works
across the **entire snapshot size range** (up to the ingest cap), not just short
snapshots that would fit in a URL. There are two fidelity tiers, chosen per row
by whether it carries a **request snapshot**.

**Faithful replay (snapshot present).** As of CS36 (LRN-057) the PDP captures a
canonical JSON **snapshot of the whole `AccessRequest`** — subject (incl.
`roles` and any on-behalf-of actor), action, resource (incl. `amount`, `maker`,
`status`, and a distinct `tenant`/`branch`), and context `scopes` — alongside
each decision. When a row carries one, the Audit Explorer reconstructs the
**original request 1:1**, recovering every ABAC input the older best-effort
pre-fill could not. The pre-filled page notes that the replayed request is
**reconstructed context, not part of the tamper-evident hash** (see below).

**Best-effort replay (no snapshot).** Rows without a snapshot — older entries,
non-PDP producers, or a snapshot that failed open to null (see below) — fall
back to the original behavior: the Playground is pre-filled only with the
**captured** audit fields (subject, action, resource type/id, and the single
`tenant` mapped to the subject), and a banner states that the ABAC inputs
(`amount`, `maker`, `status`, subject `roles`, context `scopes`, and a distinct
resource `tenant`/`branch`) are not captured and must be completed to reproduce
the original decision — a cross-tenant `TenantMismatch` needs a resource tenant
distinct from the subject.

### The snapshot is non-authoritative (not hashed)

The request snapshot is a **replay convenience, not audit-of-record**. It is
persisted as a **nullable, NON-hashed column** — the same posture as the
server-stamped `ReceivedAtUtc` — and is **deliberately excluded** from
`AuditHashChain.ComputeRowHash`. Adding or changing a snapshot therefore never
alters a row's `RowHash` and `/verify` stays green: the tamper-evident chain
still binds exactly the CS13 content fields (`subject id / action / resource
type+id / tenant / decision / reason / trace / producer`) and nothing more. The
replay UI labels the reconstructed request accordingly so a snapshot is never
mistaken for chain-protected evidence.

Two safeguards keep the snapshot from ever endangering the audit write, both
**failing open to null** (the row is still audited; replay just degrades to
best-effort) and both emitting a **sanitized `ILogger` warning** so the
degradation stays observable:

- **Serialization fail-open.** If snapshot serialization throws for any reason,
  the PDP captures `null` rather than faulting the decision path, and logs a
  sanitized `serializer-failure` warning.
- **Size guard.** The ingest endpoint drops a snapshot longer than the
  **effective** cap (`RequestSnapshotOptions.EffectiveMaxSnapshotChars` — the
  configurable `MaxSnapshotChars`, bound from the `Audit:RequestSnapshot`
  section (default 16 KB = `RequestSnapshotGuard.DefaultMaxSnapshotChars`),
  **clamped to `[1, DefaultMaxSnapshotChars]`** so a misconfigured value can
  never exceed the persisted column width and fail the write) to `null` rather
  than persisting an unbounded blob, logging a sanitized `oversize` warning.
  Real snapshots are a few hundred bytes.

The snapshot carries only the request attributes already present in the decision
inputs (no new PII is introduced), consistent with the data-minimization posture
of the audit store.

## Fail-closed behavior

The Bank.Web typed clients (`IPdpClient`, `IAuditClient`) fail closed to `null`
on any non-success response, transport error, or malformed JSON. The pages
render that as an **explicit "unavailable"** state — never a fabricated
permit/allow, never a silently empty table:

- Playground: a failed fan-out shows a "PDP unavailable — the fan-out request
  failed (fail-closed)" alert.
- Audit Explorer: a failed query shows "Audit service unavailable — the query
  failed (fail-closed)"; a failed verification shows "Chain verification
  unavailable"; a broken chain shows the red **BROKEN** badge.

## How to run / try it

Both surfaces are part of the default Aspire stack: `bank-web` references the
`audit-service` (which starts by default), and the PDP forwards every real
decision to it.

```
aspire run
```

Open the `bank-web` endpoint from the Aspire dashboard, sign in (see the
[Bank.Web demo users](bank-web.md#demo-users-realm-authz-bank-password-passw0rd)),
and use the nav links to **AuthZ Playground** and **Audit Explorer**.

Audit entries come from real PDP-mediated decisions — for example the
Governance.Service segregation-of-duties checks. The Playground itself, being a
what-if surface, does **not** populate the audit log. To exercise the `opa` or
`openfga` engines in the Playground, start their (explicit-start) container
first; otherwise they render an Unavailable row.
