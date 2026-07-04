# CS19 — Agent + non-agent access

**Status:** active
**Owner:** yoga-ae
**Branch:** cs19/content
**Started:** 2026-07-04
**Closed:** —
**Phase:** Cross-cutting
**Lane:** Cross-cutting
**Depends on:** CS03, CS05, CS13, CS14

## Goal

Authorize AI agents / MCP tools / workload identities alongside humans, with on-behalf-of delegation. The OBO mechanism is defined here and reused by CS21 (no duplication).

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | omni-ae (rubber-duck) | 331dc47bee31 | 2026-07-02T19:47:54Z | Go | Owns OBO definition, references CS21 reuse, depends only on prerequisites, and does not create a reverse edge. |

## Deliverables

- Workload/client-credentials identities in Keycloak; scoped, time-boxed agent tokens.
- On-behalf-of (agent acts for a user) flow.
- PDP scenarios for non-human subjects; both human and agent paths showcased.

## Exit criteria

- An agent identity performs a scoped, audited action on behalf of a user; the human path is unaffected.

## Tasks

| Task | State | Owner | Notes |
|------|-------|-------|-------|
| Wave A — PDP agent/OBO contract + reference-provider delegation + audit + scenarios | pending | — | agent-id=yoga-ae/cs19-pdp \| role=implementer \| report-status=pending \| learnings=0; owns src/AuthzEntitlements.Authz.Pdp/** + tests/AuthzEntitlements.Authz.Pdp.Tests/**; delivers Actor contract, constrained-delegation semantics, audit actor fields, agent scenario catalog |
| Wave B — Keycloak agent workload client + delegated scopes + doc | pending | — | agent-id=yoga-ae/cs19-realm-docs \| role=implementer \| report-status=pending \| learnings=0; owns infra/keycloak/** + docs/authz/agent-and-nonhuman-access.md; runs concurrently with Wave A |
| Wave C — Bank.Web OBO showcase + Bank.Api/Gateway actor-claim helpers | pending | — | agent-id=yoga-ae/cs19-web \| role=implementer \| report-status=pending \| learnings=0; owns src/AuthzEntitlements.Bank.Web/** + Bank.Api/Gateway Auth actor helpers + web/api/gateway tests; depends on Wave A contract |
| Close-out: docs + restart state | pending | — | Update WORKBOARD.md, CONTEXT.md, and relevant docs so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | pending | — | File/disposition learnings in LEARNINGS.md and create planned follow-up CSs for unresolved issues |

## Notes / Learnings

### Design (orchestrator, 2026-07-04)

**Model — constrained delegation, not impersonation.** An agent acting on behalf of a
user is authorized only when BOTH the effective user is permitted AND the agent holds the
delegated scope for that action class (intersection). The agent can never exceed the user's
rights, and a scoped agent can be strictly narrower ("scoped, time-boxed agent tokens").

**PDP contract (Wave A).** Add `Actor(Type, Id, Scopes)` and an optional `Subject.Actor`
(null ⇒ direct human/service path, byte-identical to today — protects "human path
unaffected"). Non-human-acting-as-itself is `Subject.Type = "service"|"agent"` with
`Actor = null`. OBO is `Subject` = the human + `Subject.Actor` = the agent. The reference
provider wraps the existing switch: compute the base (human) decision unchanged; if `Actor`
is present and the base is Permit, require `RequiredAgentScope(action)` ∈ `Actor.Scopes`
else Deny `DelegationScopeMissing`; a base Deny passes through. New reason
`DelegationScopeMissing` → determining rule `delegation-scope`. Agent delegated scopes:
`agent.bank.read` / `agent.bank.transactions.write` / `agent.bank.approvals.write`.

**Audit (Wave A).** Extend `PdpDecisionAuditEvent` additively with `SubjectType`,
`ActorId?`, `ActorType?`; populate in `PdpDecisionService`; log in the logging sink. Additive
only — do NOT alter Audit.Service's hash-chain/schema (CS13); the HTTP-forwarding sink must
keep working (extra JSON fields tolerated). Persisting actor fields in the tamper-evident
store is a documented follow-up.

**Identity (Wave B).** Add a `bank-agent` confidential client (serviceAccountsEnabled /
client-credentials) with an `agent-claims` scope emitting `subject_type=agent`, plus the
delegated `agent.bank.*` client scopes. OBO user-binding uses RFC 8693 token exchange in
production; for the offline/deterministic demo the OBO binding is modeled at the app/PDP
layer (documented). Deterministic no-Docker default path is unaffected.

**Showcase + reuse (Wave C).** Bank.Web `AgentAccess` page evaluates the SAME action as the
human directly (Actor=null) vs. via the agent (Actor set), rendering decision + explanation +
that the decision is audited with the actor recorded. Actor-claim helpers (`subject_type`,
`on_behalf_of`/`act`) are defined + unit-tested here as the OBO mechanism CS21 reuses (no
gateway-enforcement rewiring — that is CS21's scope).

**Carried learnings:** LRN-010 (`MapInboundClaims=false` for any new claim readers),
LRN-011 (bind to token, fail-closed on missing/blank actor/OBO claims).

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae |
| Reviewer agent | rubber-duck |
| Notes | Planned intent at claim time; finalized at close-out with the models/agents actually used. |

## Plan-vs-implementation review

_Pending — completed via the GPT-5.5 close-out gate before the `active → done` rename (see OPERATIONS.md § Plan-vs-implementation review). NEEDS-FIX blocks close-out._
