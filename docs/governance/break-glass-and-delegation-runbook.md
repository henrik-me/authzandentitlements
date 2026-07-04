# Break-glass & delegation runbook

> **Scope:** the operator runbook for the CS21 **break-glass** (emergency elevation) and
> **manager → delegate delegation** controls shipped across the PDP
> ([`AuthzEntitlements.Authz.Pdp`](../../src/AuthzEntitlements.Authz.Pdp)), the governance service
> ([`AuthzEntitlements.Governance.Service`](../../src/AuthzEntitlements.Governance.Service)), and the
> Bank.Web product UI ([`AuthzEntitlements.Bank.Web`](../../src/AuthzEntitlements.Bank.Web)). Both
> controls reuse the CS19 on-behalf-of (OBO) seam documented in
> [Agent & non-human access](../authz/agent-and-nonhuman-access.md); read that first for the
> `Subject` / `Actor` request shape and the constrained-delegation intersection. For the surrounding
> just-in-time (JIT) access-request and segregation-of-duties (SoD) model see
> [Access governance](access-governance.md).

This runbook tells an operator **when** to invoke break-glass, **what it can and cannot do**, and
**how** the mandatory post-review and audit trail work — and how a manager delegates a bounded
capability to a delegate. It is accurate to the shipped code: every route, field, reason code, and
obligation named below exists verbatim in the sources it links.

## Purpose

Break-glass grants a **bounded, auto-expiring emergency elevation** so an operator can act during an
incident when their standing access is missing a capability — and it forces that emergency access to
be **reviewed after the fact** and **heightened-audited**. Delegation lets a manager lend a specific
capability to a delegate for a bounded window, enforced as an OBO intersection plus an active grant.

Both are deliberately **narrow**: break-glass elevates only a *missing capability*, and delegation
only lets a delegate do what the manager already may and the grant explicitly covers. Neither can
bypass a fintech integrity invariant.

## When to invoke break-glass

Invoke break-glass **only** for a genuine, time-critical emergency where the actor's standing access
lacks a capability they legitimately need — for example:

- An incident responder must **read** a customer account to triage a failed settlement but their
  token carries no read scope.
- An on-call operator must perform an action gated to a role they do not hold, and waiting for the
  standing JIT approval flow ([Access governance](access-governance.md)) would breach the incident's
  time bound.

Do **not** use break-glass as a shortcut around normal approval, and do **not** expect it to override
an integrity rule (see the next section) — it will not, by design. Every invocation is logged at
Warning level and **must** be reviewed, so treat it as an accountable, exceptional action.

Each grant carries a mandatory **justification** (e.g. an incident id) that is surfaced on the permit
reason and preserved on the grant for review.

## What break-glass CAN and CANNOT do

Break-glass is applied **after** the base decision, in
[`ReferenceDecisionProvider.ApplyBreakGlass`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs).
It raises a base **Deny** to a **Permit** — carrying reason `BreakGlassInvoked` and the
`require_break_glass_review` obligation — **only** when **all** of these hold:

- the base deny's primary reason is a **missing capability**: `MissingScope` **or**
  `RoleNotAuthorized` (the *elevatable* set); and
- an active, matching break-glass grant is present in context: it names the request's subject
  (`SubjectId == Subject.Id`) and action (`Action == the request action`), and has **not** expired
  against the injected clock (`Now < ExpiresAt`).

**It can:**

- Elevate a `MissingScope` deny (the subject lacks a required scope).
- Elevate a `RoleNotAuthorized` deny (the subject lacks an eligible role).

**It can NOT** (the base deny **stands** even under an active grant — these are the integrity
invariants deliberately absent from the elevatable set):

- `TenantMismatch` — break-glass never bypasses **tenant isolation**.
- `MakerEqualsChecker` / `SodConflict` — never bypasses **maker-checker / segregation of duties**.
- `SubjectNotMaker` — never lets a caller act as another subject's maker.
- `NotPending` — never re-opens an already-decided transaction.
- `UnknownAction` — never permits an action outside the known vocabulary.

An expired grant, a grant issued to a **different** subject, or a grant for a **different** action
does **not** elevate — the deny stands (fail-closed). This is the core fintech control: **emergency
access grants a missing capability; it does not break segregation-of-duties or tenant isolation.**

The elevatable-reason set and the integrity invariants are defined in `ReferenceDecisionProvider`
(`ElevatableReasons`); the reason codes are in
[`Reason.cs`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/Reason.cs) and the obligation id in
[`Obligation.cs`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/Obligation.cs).

## Auto-expiry

A break-glass grant is **time-boxed**. It is issued with a positive `durationMinutes`; its
`expiresAt` is `grantedAt + durationMinutes`
([`BreakGlassGrantStore.Issue`](../../src/AuthzEntitlements.Governance.Service/BreakGlass/BreakGlassGrantStore.cs)).

Expiry is enforced at **read time** against an **injected decision clock**, never a background
sweeper and never a wall-clock read:

- The PDP checks `Now < ExpiresAt`, where `Now` is `EvaluationContext.Now` — the single source of
  "the current time" the caller passes in. Expiry uses strict `<`, so the exact expiry instant is
  already expired. This keeps the decision a **pure, deterministic function** of the request
  ([`EvaluationContext.cs`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/EvaluationContext.cs)).
- The governance store computes `IsActive(now) => now < ExpiresAt` the same way
  ([`BreakGlassGrant.cs`](../../src/AuthzEntitlements.Governance.Service/Domain/BreakGlassGrant.cs)),
  so an expired grant simply stops being active on the next read.

Because expiry is clock-injected, the Bank.Web `/delegation` page demonstrates it by advancing the
clock one second past a grant's expiry and re-evaluating — the decision flips to a deny with no
wall-clock wait.

## Mandatory post-review

Every break-glass grant carries a **mandatory post-review** — an emergency elevation always leaves an
accountable trail.

- **What must be reviewed.** A grant **requires review** once it has left its active window
  (used/expired) and no reviewer has recorded an outcome:
  `RequiresReview(now) => now >= ExpiresAt && ReviewedAt is null`
  ([`BreakGlassGrant.cs`](../../src/AuthzEntitlements.Governance.Service/Domain/BreakGlassGrant.cs)).
- **The pending-review queue.** A reviewer polls
  `GET /api/governance/break-glass/pending-review`, which returns exactly the grants whose review is
  still outstanding
  ([`BreakGlassGrantStore.ListRequiringReview`](../../src/AuthzEntitlements.Governance.Service/BreakGlass/BreakGlassGrantStore.cs)).
- **How to review.** Post to `POST /api/governance/break-glass/{id}/review` with `reviewedBy` and a
  free-form `outcome` (e.g. `approved` / `rejected`). The reviewer and outcome are recorded on the
  grant. A **second** review of an already-reviewed grant is rejected with **409 Conflict** — the
  first, accountable review is never silently overwritten. A blank `reviewedBy` or `outcome` is a
  **400**, and an unknown grant id is a **404**.
- **Who reviews.** A reviewer distinct from the operator who invoked break-glass (typically a
  `ComplianceOfficer` or `BranchManager`), consistent with the maker-checker separation the rest of
  the governance model enforces.

The Bank.Web `/break-glass` page surfaces the grant list and a review form so an operator can record
the review directly.

## The audit trail

Break-glass is **heightened-audited**. Every PDP decision already emits a structured audit event
([`PdpDecisionAuditEvent`](../../src/AuthzEntitlements.Authz.Pdp/Audit/PdpDecisionAuditEvent.cs)); a
break-glass elevation additionally sets:

- `BreakGlass` — `true` **only** when the decision was an actual `BreakGlassInvoked` permit (not
  merely because a grant was present in context);
- `BreakGlassGrantId` — the id of the invoked grant;
- `DelegationId` — the id of any manager → delegate grant carried in context.

The logging sink emits a break-glass decision at **`Warning`** level (every normal decision stays at
`Information`), so the emergency access stands out in the log stream
([`LoggingPdpDecisionAuditSink`](../../src/AuthzEntitlements.Authz.Pdp/Audit/LoggingPdpDecisionAuditSink.cs)).
These fields are **additive** — they do not alter the CS13 `Audit.Service` hash-chain schema, and the
HTTP-forwarding sink tolerates the extra JSON fields.

The governance service independently emits an audit-ready `GovernanceDecision` event for each grant
issue and review
([`BreakGlassDelegationEndpoints`](../../src/AuthzEntitlements.Governance.Service/Endpoints/BreakGlassDelegationEndpoints.cs)).

## Delegation (manager → delegate)

A manager delegates a bounded capability to a delegate who then acts **on behalf of** the manager,
reusing the CS19 OBO seam. The effective decision is the **intersection** of three checks
([`ReferenceDecisionProvider.Evaluate`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs)):

1. **Manager rights (base).** The manager's own base decision — the delegate can never exceed it.
2. **Delegate scope (CS19 OBO).** The delegate (the `Actor`) must hold the delegated `agent.bank.*`
   scope the action class requires, else `DelegationScopeMissing`.
3. **Active, matching grant (CS21).** When a delegation grant is in context it must be active and
   match this manager (`ManagerId == Subject.Id`), this delegate (`DelegateId == Actor.Id`), and be
   unexpired (`Now < ExpiresAt`), else **`DelegationNotActive`** — fail-closed on an
   absent-but-supplied, expired, or mismatched grant.

A delegation grant is created with a set of delegated scopes and a positive duration
([`DelegationGrantStore.Create`](../../src/AuthzEntitlements.Governance.Service/Delegation/DelegationGrantStore.cs));
a **self-delegation** (manager == delegate) or an **empty scope set** is rejected **400**. A grant can
be revoked early (`revokedBy`); revoking an already-revoked grant is **409**. A revoked or expired
grant is simply **not passed into the PDP context**, so the delegate loses the borrowed capability.

**Emergency-elevated OBO — the composition note.** A break-glass elevation applies to the **base**
decision and is independent of the `Actor`. It does **not** remove the OBO constraints: a delegate
acting under an emergency-elevated base **still** needs the delegated scope, and — when a delegation
grant is supplied — an active, matching one. Break-glass grants a *missing capability on the base*; it
does not widen a delegation. The two controls compose; neither bypasses the other.

## The endpoints & the Bank.Web pages

All grant endpoints live under `/api/governance` and are **anonymous** (called intra-cluster by the
PDP-context builder and the Bank.Web pages), consistent with the other governance grant endpoints
([`BreakGlassDelegationEndpoints`](../../src/AuthzEntitlements.Governance.Service/Endpoints/BreakGlassDelegationEndpoints.cs)).
The request/response shapes are in
[`Contracts/Dtos.cs`](../../src/AuthzEntitlements.Governance.Service/Contracts/Dtos.cs).

| Method + path | Purpose |
|---|---|
| `POST /api/governance/break-glass` | Issue a bounded, auto-expiring emergency grant. |
| `GET /api/governance/break-glass` | List break-glass grants (`?activeOnly=true` filters). |
| `GET /api/governance/break-glass/pending-review` | Grants whose mandatory review is outstanding. |
| `GET /api/governance/break-glass/{id}` | One break-glass grant by id. |
| `POST /api/governance/break-glass/{id}/review` | Record the mandatory post-review. |
| `POST /api/governance/delegations` | Create a manager → delegate delegation grant. |
| `GET /api/governance/delegations` | List delegation grants (`?activeOnly=true` filters). |
| `GET /api/governance/delegations/{id}` | One delegation grant by id. |
| `POST /api/governance/delegations/{id}/revoke` | Revoke a delegation early. |

Request bodies (fields are camelCase on the wire):

- **Issue break-glass** — `principalId`, `tenantCode`, `action`, `justification`, `durationMinutes`.
- **Review break-glass** — `reviewedBy`, `outcome`.
- **Create delegation** — `managerId`, `delegateId`, `tenantCode`, `scopes`, `durationMinutes`.
- **Revoke delegation** — `revokedBy`.

The Bank.Web product UI wires these into two showcase pages
([`AuthzEntitlements.Bank.Web`](../../src/AuthzEntitlements.Bank.Web)):

- **`/break-glass`** — evaluate a denied action, issue a break-glass grant, re-evaluate to see the
  `BreakGlassInvoked` permit + the `require_break_glass_review` obligation, and record the mandatory
  review from the grant list.
- **`/delegation`** — create a manager → delegate grant, then evaluate the same action as the manager
  directly, via the delegate under the active grant (Permit), and via the delegate after the grant
  auto-expires (`DelegationNotActive`), with a lifecycle list + revoke.

The request authoring and decision mapping live in offline-testable view models
(`ViewModels/BreakGlassModel.cs`, `ViewModels/DelegationModel.cs`); the typed
`GovernanceClient` calls the endpoints above and fails closed.

## Deferred / production notes

- **In-memory grant stores → EF persistence.** Both grant stores are deliberately **in-memory**
  (singletons) to preserve the deterministic no-Docker path and avoid an EF/Postgres migration in
  CS21. Persisting them in the `governance` database (mirroring the `AccessGrant` tables) is a
  documented follow-up — grants do not survive a service restart today.
- **Production OBO via RFC 8693 token exchange.** As in CS19, the manager → delegate binding is
  modeled at the **app / PDP-context layer** (`Subject.Actor` + the delegation grant), not minted into
  a token. The production form is **OAuth 2.0 Token Exchange (RFC 8693)**, where the exchanged token
  itself sets `sub` = the manager and `act` = the delegate. That exchanged-token shape is the
  documented production path; the deterministic lab does not require it. See
  [Agent & non-human access › RFC 8693 note](../authz/agent-and-nonhuman-access.md#rfc-8693-note).
- **Reviewer identity binding.** As with the JIT approval flow, the service records the claimed
  `reviewedBy` / `revokedBy` but does not yet cryptographically bind it to an authenticated caller —
  that binding is the same cross-cutting authN concern deferred in
  [Access governance](access-governance.md).
