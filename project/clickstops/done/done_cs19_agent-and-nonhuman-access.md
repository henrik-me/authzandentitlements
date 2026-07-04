# CS19 — Agent + non-agent access

**Status:** done
**Owner:** yoga-ae
**Branch:** cs19/content
**Started:** 2026-07-04
**Closed:** 2026-07-04
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
| Wave A — PDP agent/OBO contract + reference-provider delegation + audit + scenarios | complete | yoga-ae/cs19-pdp | agent-id=yoga-ae/cs19-pdp \| role=implementer \| report-status=complete \| learnings=2; Actor/AgentScopeNames, constrained-delegation wrapper, DelegationScopeMissing, additive audit actor fields, AgentAccessScenarioCatalog + PDP tests |
| Wave B — Keycloak agent workload client + delegated scopes + doc | complete | yoga-ae/cs19-realm-docs | agent-id=yoga-ae/cs19-realm-docs \| role=implementer \| report-status=complete \| learnings=1; bank-agent + agent-claims/agent.bank.* scopes, service-claims for bank-workload, README + design doc |
| Wave C — Bank.Web OBO showcase + Bank.Api/Gateway actor-claim helpers | complete | yoga-ae/cs19-web | agent-id=yoga-ae/cs19-web \| role=implementer \| report-status=complete \| learnings=1; AgentAccess page + AgentAccessModel + PdpActorDto, ActorClaims/GatewayActorClaims + web/api/gateway tests |
| Review fixes — delegate-kind allow-list + CWE-117 log sanitization | complete | yoga-ae/cs19-fix | agent-id=yoga-ae/cs19-fix \| role=implementer \| report-status=complete \| learnings=2; R1 OBO gate {agent,service} ordinal allow-list; Copilot/CodeQL doc + log-forging (CR/LF-sanitize all rendered log fields) |
| Close-out: docs + restart state | complete | yoga-ae | Updated WORKBOARD.md + CONTEXT.md so a fresh agent can restart from actual state |
| Close-out: learnings + follow-ups | complete | yoga-ae | Filed LRN-058..060 in LEARNINGS.md; OBO mechanism reused by the already-planned CS21 |

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
`on_behalf_of`, `sub`; delegation resolves only for a recognized delegate kind `agent`/`service`,
fail-closed) are defined + unit-tested here as the OBO mechanism CS21 reuses — the RFC-8693 `act`
nested claim is the production/exchanged-token form documented for CS21 (no gateway-enforcement
rewiring — that is CS21's scope).

**Carried learnings:** LRN-010 (`MapInboundClaims=false` for any new claim readers),
LRN-011 (bind to token, fail-closed on missing/blank actor/OBO claims).

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8, claude-opus-4.6 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae |
| Reviewer agent | rubber-duck |
| Notes | Waves A/B/C claude-opus-4.8; review-fix waves claude-opus-4.6/4.8. Independent GPT-5.5 rubber-duck across 6 impl rounds (R1 NF→R2 Go→R3 Go→R4 Go→R5 NF→R6 Go) + plan-vs-impl GO, interleaved with 3 Copilot rounds + CodeQL. Reviewer model disjoint from implementers (A3). |

## Plan-vs-implementation review

**Reviewer:** gpt-5.5 (rubber-duck, agent `cs19-pvi`) — independent of the claude-opus-4.8/4.6 implementers
**Date:** 2026-07-04
**Outcome:** GO

Per-deliverable outcome:

| Deliverable | Outcome | Rationale |
|---|---|---|
| Workload/client-credentials identities in Keycloak; scoped, time-boxed agent tokens | match | `bank-agent` confidential service-account (client-credentials) with `agent-claims` (`subject_type=agent`) + delegated `agent.bank.*` scopes (default `read`, write/approvals optional/per-token); `bank-workload` marked `subject_type=service` via `service-claims`; realm `accessTokenLifespan` bounded. |
| On-behalf-of (agent acts for a user) flow | match | `Actor(Type,Id,Scopes)` + optional `Subject.Actor`; `ReferenceDecisionProvider` constrained-delegation (base decision ∧ delegated scope; `DelegationScopeMissing` fail-closed; Actor==null short-circuit); `ActorClaims`/`GatewayActorClaims` read `subject_type`/`on_behalf_of`/`sub`, fail-closed, `{agent,service}` allow-list. |
| PDP scenarios for non-human subjects; both human + agent paths showcased | match | `AgentAccessScenarioCatalog` (OBO permit, missing-scope deny, human-deny passthrough, service-as-itself, approvals/write/read); Bank.Web `AgentAccess` + `AgentAccessModel` render human-direct vs agent side-by-side, differing only by `Subject.Actor`. |
| Exit criterion — scoped, audited OBO action; human path unaffected | match | Scoped = user-permit ∧ delegated scope; audited = `SubjectType`/`ActorId`/`ActorType` on the audit event, populated in `PdpDecisionService`; human path byte-identical via `Actor is null` return + actor-free catalog tests. |

**Test-coverage:** sufficient — CS19 test classes present; targeted PDP/API/Gateway/Web CS19 filters pass (61 + 18 + 13 + 12). Full solution 1064/1064 on the pre-merge branch; 1132+ on merged main.

**Scope:** no substantive drift; every deliverable + exit criterion met. The OBO mechanism is defined here and documented as the CS21 reuse point (no reverse dependency). Non-blocking follow-ups (documented): production RFC 8693 token-exchange OBO issuance, and persisting the actor fields in the tamper-evident Audit.Service store.
