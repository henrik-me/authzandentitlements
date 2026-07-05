# CS21 — Break-glass, delegation & on-behalf-of

**Status:** done
**Owner:** yoga-ae
**Branch:** cs21/content
**Started:** 2026-07-04
**Closed:** 2026-07-05
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
| Wave A — PDP break-glass elevation + delegation-grant enforcement + audit + scenarios | complete | yoga-ae/cs21-pdp | agent-id=yoga-ae/cs21-pdp \| role=implementer \| report-status=complete \| learnings=3; EvaluationContext BreakGlass/Delegation/Now, elevation of MissingScope/RoleNotAuthorized only, DelegationNotActive, heightened audit, 10-scenario catalog, 46 tests, PDP suite 772/0 |
| Wave B — Governance break-glass + delegation grant lifecycle + endpoints | complete | yoga-ae/cs21-gov | agent-id=yoga-ae/cs21-gov \| role=implementer \| report-status=complete \| learnings=3; in-memory time-boxed stores (IsActive/RequiresReview), 9 anonymous endpoints, 6 DTOs, 66 tests, Governance suite 162/0 |
| Wave C — Bank.Web UX + PDP/Governance wiring + runbook | complete | yoga-ae/cs21-web | agent-id=yoga-ae/cs21-web \| role=implementer \| report-status=complete \| learnings=3; PdpContextDto mirror + 7 GovernanceClient methods, BreakGlass/Delegation pages + VMs, runbook (fact-checked), 22 tests, Bank.Web suite 154/0 |
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
UnknownAction}` are **never** overridden. Because `EvaluateCore` surfaces only the FIRST failing
reason and capability gates run before integrity gates, elevation is NOT decided on the primary
reason alone: an independent hard-invariant guard (`PassesHardInvariants`) re-checks every integrity
invariant for the action and refuses elevation if any would fail, so a missing-capability denial can
never *mask* (and thereby bypass) a co-occurring integrity violation. Expired/absent grant ⇒ no
elevation. This is the core fintech control: emergency access grants missing capability, it does not
break segregation-of-duties or tenant isolation.

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
| Implementer models | claude-opus-4.8, claude-sonnet-4.5 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae/cs21-pdp, yoga-ae/cs21-gov, yoga-ae/cs21-web |
| Reviewer agent | rubber-duck |
| Notes | Waves A/B/C dispatched claude-opus-4.8; B/C sub-agents materially reported claude-sonnet-4.5. Reviewer gpt-5.5 is disjoint from every implementer model (A3 independence). |

## Plan-vs-implementation review

**Reviewer:** gpt-5.5 (rubber-duck, agent `cs21-plan-vs-impl`) — independent of the claude-opus-4.8/4.6 + claude-sonnet-4.5 implementers
**Date:** 2026-07-05
**Outcome:** GO

Per-deliverable outcome:

| Deliverable | Outcome | Rationale |
|---|---|---|
| Break-glass: heightened audit + auto-expiry + mandatory post-review | match | PDP elevates only `MissingScope`/`RoleNotAuthorized` under an active matching grant, gated by `PassesHardInvariants` (no integrity bypass), emitting `BreakGlassInvoked` + `require_break_glass_review`; heightened audit (`BreakGlass`/`BreakGlassGrantId` at Warning); governance grants auto-expire (`IsActive(now)`) and are reviewable only post-expiry (`RequiresReview`). |
| Delegation (manager→delegate) + OBO integration | match | Reference provider enforces OBO actor scope ∧ the grant's own `Scopes` ∧ active matching manager/delegate grant ∧ non-blank `GrantId` ∧ injected-clock expiry, else `DelegationScopeMissing`/`DelegationNotActive`; CS19 agent-OBO byte-identical when no delegation grant present. |
| PDP + Governance enforcement + product UX + runbook | match | Additive/defaulted `EvaluationContext`; new reason/obligation contracts; 9 governance endpoints (issue/list/pending-review/review + create/list/revoke); Bank.Web `PdpContextDto` mirror + `GovernanceClient` + `/break-glass` & `/delegation` pages; runbook `docs/governance/break-glass-and-delegation-runbook.md`. |
| Exit criterion — break-glass works/auto-expires/forces post-review; delegation + OBO enforced + audited | match | Scenario catalog + tests cover elevation, expiry boundary, integrity guard, masking-bypass regression, delegation active/expired/mismatch/scope-ceiling, blank-grant-id fail-closed, and the byte-identical human path. |

**Test-coverage:** sufficient — full solution `dotnet test` **1484/0**; targeted PDP/Governance/Bank.Web CS21 suites green; break-glass/delegation are reference-provider-only (engine parity isolated to actor/grant-free scenarios).

**Scope:** no drift. Documented follow-ups (in the runbook): EF persistence for the in-memory grant stores; production RFC-8693 token-exchange OBO issuance; PDP-authoritative delegation revocation; reviewer-identity binding.

**Review rounds:** 11 independent GPT-5.5 rubber-duck diff reviews (R1 Needs-Fix → R2–R11 Go) — R1 caught a break-glass integrity-masking bypass (fixed with `PassesHardInvariants`); GitHub Copilot review across 8 rounds surfaced store encapsulation, post-review timing, blank-grant-id fail-closed, delegated-scope-ceiling, retention-vs-mandatory-review, and a CWE-117 governance-audit log-forging vector — all fixed. Reviewer model disjoint from every implementer (independence invariant A3).
