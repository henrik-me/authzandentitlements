# Access governance

> **Scope:** the access-governance entitlement model shipped in CS11 by
> [`AuthzEntitlements.Governance.Service`](../../src/AuthzEntitlements.Governance.Service) — access
> packages, just-in-time (JIT) elevation gated by a maker-checker + segregation-of-duties (SoD)
> approval, time-bound grants whose expiry is enforced at read time, and access-review
> (recertification) campaigns. The SoD gate is answered by the unified PDP over HTTP, so read the
> [PDP contract](../authz/pdp-contract.md) and the [OPA/Rego adapter](../authz/opa-adapter.md)
> first: the SoD verdict is identical whether the PDP runs the in-process reference engine (the
> default) or the out-of-process OPA engine.

This is the "Entra-style" governance layer of the lab's two-sense
[entitlements story](../../ARCHITECTURE.md#components) (commercial entitlements are the other
sense, owned by `Entitlements.Service`). It answers *who may temporarily hold which elevated roles,
for how long, and who signed off?* — and refuses any elevation that would put a toxic combination of
duties in one pair of hands.

## What CS11 ships (and what it does not)

CS11 delivers the governance **service and its lifecycle**:

- **Access packages** — named bundles of fintech roles a principal can request (e.g.
  `quarter-end-close`).
- **JIT elevation with maker-checker approval** — a request is created `Pending`; an approver who
  must differ from the requester approves it.
- **SoD via the PDP** — approval runs a segregation-of-duties check over the proposed resulting role
  set through the PDP; the check is **fail-closed**.
- **Time-bound grants + read-time expiry** — an approved request issues a grant that expires after a
  bounded window; expiry is enforced when access is read, with no background sweeper.
- **Access-review / recertification campaigns** — a campaign materialises one review item per active
  grant in a tenant; each item is certified (kept) or revoked.

Out of scope for CS11 (owned by later clickstops):

- **Break-glass, delegation, and on-behalf-of** access — a later clickstop (CS21). ARCHITECTURE.md
  lists them under `Governance.Service`, but they are **not** part of CS11.
- **Bank.Api enforcement of governance grants** — CS11 ships the service and the grant lifecycle;
  wiring `Bank.Api` to treat an active grant as effective elevation at request time is deferred to a
  later clickstop.
- **Audit ingestion** — [`Audit.Service`](../../ARCHITECTURE.md) ingests the emitted decision events
  in CS13; CS11 only **emits** them in an audit-ready shape (see [Audit + telemetry](#audit--telemetry)).
- **The persistent observability stack** — the collector + dashboards land in CS12; CS11 emits OTel
  metrics that fan out to it once it is running.

## The service

[`AuthzEntitlements.Governance.Service`](../../src/AuthzEntitlements.Governance.Service) is an
ASP.NET Core minimal-API service on `net10.0`. It owns the `governance` Postgres database (injected
by Aspire via `.WithReference(governanceDb)`) and is registered in the AppHost as `governance-service`.
Its only outbound dependency is the PDP, reached over Aspire service discovery at
`https+http://authz-pdp` — see [`Program.cs`](../../src/AuthzEntitlements.Governance.Service/Program.cs).
On startup it applies EF Core migrations and runs an idempotent
[seeder](../../src/AuthzEntitlements.Governance.Service/Data/GovernanceSeeder.cs); both are no-ops
when the database is already current.

The endpoints are anonymous: the service is called intra-cluster, and edge/token concerns are
handled by the gateway and other clickstops. The full surface lives under `/api/governance` (see
[`GovernanceEndpoints.cs`](../../src/AuthzEntitlements.Governance.Service/Endpoints/GovernanceEndpoints.cs)):

| Method + path | Purpose |
|---|---|
| `GET /api/governance/access-packages` | List the access-package catalog. |
| `GET /api/governance/access-packages/{code}` | One access package by code. |
| `POST /api/governance/requests` | Create a JIT elevation request (`Pending`). |
| `GET /api/governance/requests` | List requests. |
| `GET /api/governance/requests/{id}` | One request. |
| `POST /api/governance/requests/{id}/approve` | Approve: maker-checker → SoD → issue a grant. |
| `POST /api/governance/requests/{id}/reject` | Reject: maker-checker, no grant. |
| `GET /api/governance/principals/{id}/grants` | A principal's grants (`?activeOnly=true` filters). |
| `GET /api/governance/principals/{id}/access` | Effective access now (baseline ∪ active grants). |
| `POST /api/governance/grants/{id}/revoke` | Revoke a grant early. |
| `POST /api/governance/review-campaigns` | Create a recertification campaign (`Open`). |
| `GET /api/governance/review-campaigns` | List campaigns. |
| `GET /api/governance/review-campaigns/{id}` | One campaign with its items. |
| `POST /api/governance/review-campaigns/{id}/run` | Materialise one review item per active grant. |
| `POST /api/governance/review-items/{id}/decision` | Certify or revoke a review item. |

## The lifecycle

The end-to-end path from a request to an expired grant:

1. **Request** — `POST /requests` with `principalId`, `accessPackageCode`, a required `justification`,
   and an optional `requestedDurationMinutes`. The request is created `Pending` with an SoD outcome of
   `NotEvaluated`. The principal and package must already exist (unknown ids fail closed with 404).
2. **Maker-checker** — `POST /requests/{id}/approve` (or `/reject`) with an `approverId`. The approver
   **must differ from the requester** (the trusted `principalId` on the stored request, never a body
   field) — a self-approval is denied `403` and leaves the request `Pending`. This is the SoD gate on
   the *approval action itself*
   ([`GovernanceRules.CheckerDiffersFromRequester`](../../src/AuthzEntitlements.Governance.Service/Domain/GovernanceRules.cs)).
3. **SoD via the PDP** — the approver having cleared maker-checker, the service computes the
   **proposed resulting role set** (the principal's baseline roles ∪ the package's roles) and asks the
   PDP whether that set is internally compatible (see [below](#segregation-of-duties-via-the-pdp)).
   The call is **fail-closed**: if the PDP cannot be reached the request stays `Pending` and the
   endpoint returns `503`; a genuine SoD conflict rejects the request (`409`, outcome `SodConflict`);
   only a permit proceeds.
4. **Time-bound grant** — on a permit the request becomes `Approved` and a grant is issued. Its roles
   are a **snapshot** of the package's roles at grant time, and its `expiresAt` is
   `grantedAt + effectiveDuration`, where the effective duration is `requestedDurationMinutes` when it
   is a positive override, otherwise the package's `defaultDurationMinutes`
   ([`AccessGrantFactory`](../../src/AuthzEntitlements.Governance.Service/Domain/AccessGrantFactory.cs)).
5. **Expiry (read-time)** — a grant is *active* only while it is neither revoked nor past its expiry
   (`AccessGrant.IsActive(now)`). The effective-roles read (`GET /principals/{id}/access`) and the
   active-grants read compute this at read time, so an expired grant simply stops counting — no
   background job flips its state. A grant can also be revoked early (`POST /grants/{id}/revoke`).
6. **Review campaigns** — `POST /review-campaigns` opens a campaign; `POST /review-campaigns/{id}/run`
   materialises one `Pending` review item per currently-active grant in the tenant; `POST
   /review-items/{id}/decision` certifies (keeps) or revokes (removes the linked grant) each item. When
   no item is left `Pending`, the campaign is `Completed`.

Approve/reject are **decide-once**: the request row carries the Postgres `xmin` system column as an
optimistic-concurrency token, so a second concurrent decision on the same request loses its
`SaveChanges` and surfaces a `409` instead of last-writer-wins.

## Segregation of duties via the PDP

Per the CS11 plan review, SoD is evaluated **by the PDP**, not by coupling the governance service
directly to OPA. The service calls the PDP's AuthZEN evaluate contract
([`PdpSodClient`](../../src/AuthzEntitlements.Governance.Service/Sod/PdpSodClient.cs)) with the action
**`governance.access.request`**.

Unlike the bank verbs, this action is a **pure** SoD check: it has no scope, role-eligibility,
tenant, or maker gate. It asks only *is this proposed role set internally incompatible?* The
`subject.roles` array is the proposed resulting set (baseline ∪ package roles, deduplicated and
ordered ordinally by
[`ProposedRoleSet`](../../src/AuthzEntitlements.Governance.Service/Domain/ProposedRoleSet.cs)); the
resource is the access package. A toxic combination denies `SodConflict`; an independent set permits
with no obligation.

Request as it reaches `POST /api/authz/evaluate` (an Auditor requesting a package that grants
`BranchManager`):

```json
{
  "subject": {
    "type": "user",
    "id": "user-auditor1",
    "roles": ["Auditor", "BranchManager"],
    "tenant": "CONTOSO"
  },
  "action": { "name": "governance.access.request" },
  "resource": { "type": "access-grant", "id": "branch-approver", "tenant": "CONTOSO" },
  "context": { "scopes": [] }
}
```

A conflicting set denies:

```json
{
  "decision": "Deny",
  "reasons": [
    { "code": "SodConflict", "message": "Segregation of duties: the roles 'Auditor' and 'BranchManager' may not be held together." }
  ],
  "obligations": []
}
```

An independent set permits (`{ "decision": "Permit", "reasons": [ { "code": "Permit", ... } ], "obligations": [] }`).

The client maps this decision to a local
[`SodCheckResult`](../../src/AuthzEntitlements.Governance.Service/Sod/SodCheckResult.cs): a `Permit`
is a permit; an explicit `Deny` carries the PDP's primary reason code; **anything else** — a
transport error, timeout, non-success status, empty body, unknown decision, or a deny with no reason
— fails closed as `Unavailable`, which the approval endpoint maps to a `503` (never a permit).

### The incompatible role pairs

A proposed role set conflicts when it contains **both** members of any of these unordered pairs. The
rule is defined once in
[`GovernanceSodPolicy`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Sod/GovernanceSodPolicy.cs)
and mirrored verbatim in the Rego (`sod_incompatible_pairs` in
[`authz.rego`](../../infra/opa/policy/authz.rego)):

| Role | Incompatible with |
|---|---|
| `Teller` | `BranchManager` |
| `Teller` | `ComplianceOfficer` |
| `Auditor` | `Teller` |
| `Auditor` | `BranchManager` |
| `Auditor` | `ComplianceOfficer` |

The rationale: a `Teller` (a maker) must not also hold an approval/oversight role, and an `Auditor`
must stay independent of every operational or approval role. Two oversight roles together —
`BranchManager` + `ComplianceOfficer` — are **deliberately allowed**, which is why `quarter-end-close`
(which grants both) permits. An empty or single-role set never conflicts; roles outside the listed
pairs are ignored; matching is exact and case-sensitive.

### Engine parity: reference and OPA agree

The verdict is **engine-agnostic**. The in-process
[`ReferenceDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs)
routes `governance.access.request` through `GovernanceSodPolicy`, and the OPA
[`authz.rego`](../../infra/opa/policy/authz.rego) `governance_access_decision` rule encodes the same
pairs — so both engines return the same `Permit`/`Deny SodConflict` for the same proposed role set.

The PDP's **default** provider is `reference`, so `dotnet build`, `dotnet test`, and `aspire run`
never need Docker or a live OPA. To exercise the real OPA path (per the
[OPA/Rego adapter](../authz/opa-adapter.md)):

1. Start the opt-in `opa` container (it is registered `WithExplicitStart` in the AppHost).
2. Set `Pdp__Provider=opa` on `authz-pdp`.

The governance service is unchanged either way — it always calls the same PDP endpoint, and the SoD
verdict is identical.

## Data model

The `governance` Postgres database
([`GovernanceDbContext`](../../src/AuthzEntitlements.Governance.Service/Data/GovernanceDbContext.cs))
holds:

- **Access packages** (`AccessPackages` + `AccessPackageRoles`) — a package has a unique `code`, a
  `defaultDurationMinutes`, a `requiresApproval` flag, and the fintech roles it grants.
- **Principals** (`Principals` + `PrincipalRoles`) — a principal (a user id) with its **baseline**
  (standing) roles and owning tenant.
- **Grant requests** (`AccessGrantRequests`) — the JIT requests, with status, SoD outcome/reason, the
  decider, and the `xmin` decide-once concurrency token.
- **Grants** (`AccessGrants` + `AccessGrantRoles`) — issued time-bound grants, each with a role
  snapshot, `grantedAt`/`expiresAt`, and optional `revokedAt`/`revokedBy`.
- **Review campaigns** (`AccessReviewCampaigns` + `AccessReviewItems`) — recertification campaigns and
  their per-grant review items.

Enums are persisted as strings; lookup keys are uniquely indexed; child collections cascade-delete.

### Seeded demo data

The seeder ids, roles, and tenant match the CS02/CS03 bank seed exactly, so governance reasons about
the same identities the rest of the system knows. All principals are in tenant `CONTOSO`:

| Principal | Baseline role |
|---|---|
| `user-teller1` | `Teller` |
| `user-manager1` | `BranchManager` |
| `user-compliance1` | `ComplianceOfficer` |
| `user-auditor1` | `Auditor` |

| Access package | Grants | Default duration |
|---|---|---|
| `quarter-end-close` | `BranchManager` + `ComplianceOfficer` | 480 min |
| `treasury-oversight` | `ComplianceOfficer` | 240 min |
| `branch-approver` | `BranchManager` | 120 min |

`branch-approver` is the SoD-conflict demo: a `Teller` or `Auditor` requesting it produces a proposed
set that trips an incompatible pair, so the PDP SoD check denies.

## Audit + telemetry

Every governance decision emits an **audit-ready** structured `ILogger` event named
`GovernanceDecision`
([`LoggingGovernanceAuditSink`](../../src/AuthzEntitlements.Governance.Service/Metering/LoggingGovernanceAuditSink.cs)),
carrying tenant, decision type, target, outcome, principal, reason, correlation id, and timestamp.
The decision-type and outcome values are **lower-cased kebab tokens** (e.g. `sod-deny`,
`grant-issued`) so the events match stably. `Audit.Service` ingests them in CS13; CS11 only emits.

The service also owns the `AuthzEntitlements.Governance` OpenTelemetry meter
([`GovernanceMetrics`](../../src/AuthzEntitlements.Governance.Service/Metering/GovernanceMetrics.cs))
with counters `governance.requests`, `governance.decisions` (tagged `type` + `outcome`),
`governance.grants.issued`, `governance.grants.revoked`, and `governance.reviews.run`. The meter is
added to the OTel pipeline `AddServiceDefaults` configures, so it exports to the CS12 observability
stack alongside the runtime metrics.

## Worked example

**A denied elevation (SoD).** `user-auditor1` (baseline `Auditor`) requests `branch-approver` (grants
`BranchManager`):

1. `POST /requests` → `Pending`.
2. A *different* principal approves via `POST /requests/{id}/approve` — maker-checker passes.
3. SoD runs over the proposed set `["Auditor", "BranchManager"]` → the PDP returns
   `Deny SodConflict` (the `Auditor` / `BranchManager` pair).
4. The request becomes `Rejected` with `sodOutcome = Deny`; the endpoint returns `409`. No grant is
   issued.

**A permitted, expiring elevation.** `user-manager1` (baseline `BranchManager`) requests
`quarter-end-close` (grants `BranchManager` + `ComplianceOfficer`):

1. `POST /requests` with a justification → `Pending`.
2. Another principal (e.g. `user-compliance1`) approves — maker-checker passes.
3. SoD runs over the proposed set `["BranchManager", "ComplianceOfficer"]` → **Permit** (two oversight
   roles are allowed together).
4. A grant is issued for `["BranchManager", "ComplianceOfficer"]`, expiring 480 minutes after it was
   granted. `GET /principals/user-manager1/access` shows those roles as effective — until the grant
   expires or is revoked, after which the read no longer counts it.
