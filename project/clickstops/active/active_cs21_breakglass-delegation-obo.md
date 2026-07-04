# CS21 — Break-glass, delegation & on-behalf-of

**Status:** active
**Owner:** yoga-ae
**Branch:** cs21/content
**Started:** 2026-07-04
**Closed:** —
**Phase:** Cross-cutting
**Lane:** Cross-cutting
**Depends on:** CS05, CS11, CS13, CS14, CS19

## Goal

Provide emergency break-glass access and delegation with full mechanism AND process (high-risk). Reuses the on-behalf-of mechanism from CS19.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | f03f079409b9 | 2026-07-02T19:47:54Z | Go | Now depends on CS19, reuses its OBO mechanism explicitly, and dependency graph is acyclic. |

## Deliverables

- Break-glass access: heightened audit + auto-expiry + mandatory post-review.
- Delegation (manager -> delegate); on-behalf-of integration.
- PDP + Governance enforcement; product UX + runbook.

## Exit criteria

- A break-glass grant works, auto-expires, and forces post-review; delegation + OBO enforced and audited.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Wave A — PDP break-glass elevation + delegation-grant enforcement + audit + scenarios | in_progress | yoga-ae/cs21-pdp | agent-id=yoga-ae/cs21-pdp \| role=implementer \| report-status=pending \| learnings=0 |
| Wave B — Governance break-glass + delegation grant lifecycle + endpoints | in_progress | yoga-ae/cs21-gov | agent-id=yoga-ae/cs21-gov \| role=implementer \| report-status=pending \| learnings=0 |
| Wave C — Bank.Web UX + PDP/Governance wiring + runbook | pending | yoga-ae/cs21-web | agent-id=yoga-ae/cs21-web \| role=implementer \| report-status=pending \| learnings=0 |
| Close-out: docs + restart state | pending | — | Update WORKBOARD, CONTEXT.md, and the break-glass/delegation docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md; open follow-up CSs for unresolved break-glass/delegation/OBO gaps |

## Notes / Learnings

### Design (orchestrator, 2026-07-04)

**Reuse, don't duplicate (CS19).** Break-glass and delegation both build on the CS19 OBO
seam — `Subject.Actor(Type,Id,Scopes)`, the constrained-delegation intersection in
`ReferenceDecisionProvider`, and the additive audit actor fields. `ActorClaims` already
names CS21 as the consumer ("a break-glass grant is an OBO delegation with an elevated,
expiring scope set"). The human path stays byte-identical: every new input is a defaulted,
null-valued trailing member, so existing positional construction compiles and behaves unchanged.

**PDP stays a pure, deterministic function; grants + clock are passed IN (Wave A).** Extend
`EvaluationContext` additively: `BreakGlassGrant? BreakGlass`, `DelegationGrant? Delegation`,
`DateTimeOffset? Now`. The provider never reads the wall clock — expiry is `Now < ExpiresAt`
evaluated over injected values (testable, deterministic). Absent all three ⇒ today's behavior.
The `/api/authz/evaluate` endpoint binds `AccessRequest` straight from JSON, so these context
members are the wire contract with no new server DTO.

**Break-glass = bounded emergency elevation, never an integrity bypass (Wave A).** Applied
AFTER the base decision. A base **Deny** whose reason is in the *elevatable* set
`{MissingScope, RoleNotAuthorized}` (missing capability) is raised to **Permit** when an active
(non-expired, subject+action-matching) break-glass grant is present, carrying reason
`BreakGlassInvoked` + obligation `RequireBreakGlassReview` (mandatory post-review). Integrity
invariants `{TenantMismatch, MakerEqualsChecker, SodConflict, SubjectNotMaker, NotPending,
UnknownAction}` are **never** overridden — the base Deny stands even under an active grant.
Expired/absent grant ⇒ no elevation. This is the core fintech control: emergency access grants
missing capability, it does not break segregation-of-duties or tenant isolation.

**Delegation (manager→delegate) = OBO intersection + an active grant (Wave A).** When
`EvaluationContext.Delegation` is present it is enforced orthogonally to `Actor.Type`:
`ManagerId == Subject.Id`, `DelegateId == Actor.Id`, and `Now < ExpiresAt`, else Deny
`DelegationNotActive`. The effective decision is manager-rights (base) ∧ delegate-scopes
(CS19 OBO) ∧ active delegation grant (new). CS19 agent OBO (no `Delegation` in context) is
unchanged. No new Keycloak claim kind is required for the deterministic demo — manager→delegate
is modeled at the PDP-context/Governance layer (production RFC-8693 token-exchange form is a
documented follow-up, mirroring CS19).

**Heightened audit (Wave A).** `PdpDecisionAuditEvent` gains an additive `BreakGlass` bool
(+ optional grant id); `PdpDecisionService` populates it and the logging sink flags it. Additive
only — CS13's Audit.Service hash-chain/schema is untouched (tolerates extra JSON fields).

**Governance owns grant lifecycle; in-memory stores, no EF migration (Wave B).** Break-glass
and delegation grants live in the Governance.Service as in-memory, time-boxed stores mirroring
the `AccessGrant.IsActive(now)` pattern (issue → list-active → auto-expire → mandatory
post-review for break-glass; create → list → revoke for delegation), exposed via new anonymous
governance endpoints. Deliberately in-memory to preserve the deterministic no-Docker path and
avoid an EF/Postgres migration; EF persistence is a documented follow-up. Governance domain
grant types are independent of the PDP contract types, so **Wave A ∥ Wave B** (Governance.Service
does not project-reference the PDP).

**Wiring + UX + runbook (Wave C).** Bank.Web pages let a user request emergency break-glass
(see a denied action elevate, its expiry, and the review obligation), complete the mandatory
post-review, and drive a manager→delegate delegation (delegate acts OBO). The `PdpClient` DTO
carries the new context members; grant clients call the Governance endpoints. The runbook
(`docs/governance/break-glass-and-delegation-runbook.md`) documents who may invoke, auto-expiry,
mandatory post-review, and the audit trail. Wave C depends on A (DTO/reasons) + B (endpoints).

**Engine parity is safe.** The cross-engine parity suite (`LifecycleTestSupport`) uses
actor-free, grant-free requests, so break-glass/delegation code paths are never exercised by
parity tests — the aspnet/casbin/cedar adapters are **not** touched. Wave A must still run the
full PDP test project green (existing parity + OBO + new break-glass/delegation).

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
