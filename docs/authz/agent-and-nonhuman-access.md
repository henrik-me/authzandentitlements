# Agent & non-human access

> **Scope:** how the PDP authorizes **AI agents, MCP tools, and workload identities** alongside
> humans, including **on-behalf-of (OBO)** delegation. It documents the CS19 surface in
> `AuthzEntitlements.Authz.Pdp`: the `Actor` contract and the extended `Subject`, the
> constrained-delegation semantics the reference provider enforces (intersection of the user's
> rights and the agent's delegated scopes — *not* impersonation), the actor-aware audit fields, and
> the Keycloak `bank-agent` identity that issues scoped, time-boxed agent tokens. Builds on the
> [PDP contract](pdp-contract.md) and the [audit pipeline](audit-pipeline.md). The OBO mechanism
> defined here is **reused by CS21** (break-glass / delegation) — it is defined once, here.

## Overview

A modern authorization surface has to answer for callers that are not people: an AI agent booking a
transfer, an MCP tool reading balances, a batch workload reconciling ledgers. Two things must stay
true when a non-human calls:

1. **A non-human can act as itself** — a workload identity with its own rights, no user involved.
2. **A non-human can act *for* a human (OBO)** — an agent performing an action on behalf of a user,
   **bounded** by both what the user may do and what the agent was delegated to do.

The design goal is that the **human path is byte-identical to today** — adding agents must not
change how a direct human call decides — while an OBO call is constrained to the **intersection** of
the user's rights and the agent's delegated capability. An agent can never exceed the user's rights,
and a scoped agent can be strictly *narrower*.

## Two subject shapes

The [`Subject`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/Subject.cs) record gains one
optional, defaulted trailing member — `Actor` — so every existing positional construction of a
direct human subject keeps compiling and deciding unchanged.

```csharp
public sealed record Subject(
    string Type,                        // AuthZEN subject type: "user" | "service" | "agent"
    string Id,
    IReadOnlyList<string> Roles,
    string? Tenant = null,
    string? Branch = null,
    Actor? Actor = null);               // null => direct call; non-null => OBO delegate

public sealed record Actor(
    string Type,                        // the delegate's kind: "agent" | "service"
    string Id,                          // the delegate's stable identity
    IReadOnlyList<string> Scopes);      // the DELEGATED capability scopes (agent.bank.*)
```

- **Non-human acting as ITSELF** — `Subject.Type` = `"service"` or `"agent"`, and `Actor = null`.
  The subject is the non-human principal; it is authorized by its own rights, exactly like a human
  subject is, with no delegation constraint layered on.
- **On-behalf-of (OBO)** — `Subject` is the **human** the action is performed for, and
  `Subject.Actor` is the **agent** performing it. `Actor.Scopes` is the ceiling on what the agent
  may do for that user.

## Constrained delegation, not impersonation

OBO is deliberately **not** impersonation. Impersonation would let the agent *become* the user and
inherit the user's full rights; constrained delegation intersects two independent checks. The
reference provider wraps its existing decision switch:

1. Compute the **base decision** for the effective user, unchanged (byte-identical to the direct
   human path).
2. If `Actor` is present:
   - a base **Deny passes through** untouched — an agent can never *widen* a user's denial;
   - a base **Permit** additionally requires the agent to hold the delegated scope for the action
     class: `AgentScopeNames.RequiredFor(action) ∈ Actor.Scopes`;
   - a **missing** delegated scope ⇒ **Deny** with reason `DelegationScopeMissing` (determining rule
     `delegation-scope`).

So the effective decision is `basePermit AND agentHoldsDelegatedScope`. The agent can only ever be
**as narrow as or narrower than** the user it acts for.

### Action → required delegated scope

The delegated scopes are **distinct** from the coarse OAuth scopes the human already holds. The map
lives in
[`AgentScopeNames.RequiredFor`](../../src/AuthzEntitlements.Authz.Pdp/Contracts/AgentScopeNames.cs)
and is fail-closed: an action the delegation model does not explicitly cover returns `null`, so the
caller denies (a delegate can never act on an uncovered action class).

| Action | Required delegated scope |
|---|---|
| `bank.account.read` | `agent.bank.read` |
| `bank.account.create` | `agent.bank.transactions.write` |
| `bank.transaction.create` | `agent.bank.transactions.write` |
| `bank.transaction.approve` | `agent.bank.approvals.write` |
| `bank.transaction.reject` | `agent.bank.approvals.write` |
| `governance.access.request` | `agent.bank.read` |

A worked example: a compliance officer may approve a high-value transaction (base Permit). An agent
acting for that officer with only `agent.bank.read` is **denied** `DelegationScopeMissing` — the
delegation is narrower than the user. Grant the agent `agent.bank.approvals.write` and the same OBO
call is permitted, because now *both* checks pass. Conversely, if the officer themselves is denied
(e.g. segregation-of-duties), no agent scope can rescue it — the base Deny passes through.

## Audit: the acting agent is recorded distinctly

Every PDP decision emits a structured audit event (see [audit pipeline](audit-pipeline.md)). For
agent and OBO calls the event additionally records:

- `SubjectType` — whether the subject is a human, service, or agent;
- `ActorId` — the acting delegate's identity (OBO only);
- `ActorType` — the acting delegate's kind (OBO only).

This makes the delegated action **auditable end to end**: the effective user (the `Subject`) and the
acting agent (the `Actor`) are recorded as **distinct** parties, so an auditor can answer "who *did*
this, and on whose behalf?" rather than seeing only the user. The fields are **additive** — they do
not alter the Audit.Service hash-chain schema (CS13), and the HTTP-forwarding sink tolerates the
extra JSON fields. Persisting the actor fields in the tamper-evident store is a documented follow-up.

## Identity & tokens

The non-human identity is the Keycloak **`bank-agent`** client — a confidential **service account**
using the **client-credentials** grant (no interactive login). Its realm surface is documented in the
[Keycloak realm README](../../infra/keycloak/README.md#agent--non-human-access-cs19); the essentials:

- The token carries a hardcoded **`subject_type=agent`** claim (from the `agent-claims` scope), so a
  resource server can distinguish a non-human caller from a human without guessing.
- The delegated **`agent.bank.*`** scopes are least-privilege: **`agent.bank.read` is default**, and
  `agent.bank.transactions.write` / `agent.bank.approvals.write` are **optional** scopes requested
  per token. This is the "scoped, time-boxed agent tokens" property — an elevated capability is
  minted only for the call that needs it.

### The claim contract a resource server reads

- **`subject_type`** — `agent` marks a non-human caller; its absence (or `user`) marks a human. Read
  it fail-closed: an unexpected or blank value denies rather than defaulting to human.
- For **OBO**, this lab's agent token — exactly what `ActorClaims` (Bank.Api) and
  `GatewayActorClaims` (edge gateway) read — carries `subject_type=agent`, the **agent's own
  identity in `sub`**, and the **effective user in the `on_behalf_of` claim**. So
  `TryGetDelegation` resolves the *actor* = `sub` (the agent) and the *on-behalf-of user* =
  `on_behalf_of`, fail-closed when either is missing/blank. **Note the `sub` inversion vs. RFC 8693
  below:** under token exchange the *exchanged* token instead sets `sub` = the **user** and `act` =
  the **agent**. Both bind the same two parties; only *which* token carries *which* claim differs.

### RFC 8693 note

Production OBO user-binding uses **OAuth 2.0 Token Exchange (RFC 8693)**: the agent exchanges its
token for one whose `sub` is the **user** and whose `act` / `on_behalf_of` names the **agent**, so
the exchanged token itself carries both parties. In this **offline, deterministic** lab the OBO
binding is instead modeled at the **app / PDP layer** (the `Subject` + `Subject.Actor` shapes above),
so enabling Keycloak's preview token-exchange feature is **not required** to run the demo. The claim
contract is the same either way; only *where* the binding is asserted differs.

## How to see it

- **Bank.Web "Agent access" showcase** — the `AgentAccess` page in
  [`AuthzEntitlements.Bank.Web`](../../src/AuthzEntitlements.Bank.Web) evaluates the **same action**
  two ways: as the human directly (`Actor = null`) and via the agent (`Actor` set), rendering the
  decision, the explanation, and that the decision is audited with the actor recorded. It makes the
  intersection semantics visible side by side.
- **PDP native contract** — `POST /api/authz/evaluate` accepts the same `Subject` (optionally with an
  `Actor`) and returns the self-explaining decision, including the `delegation-scope` determining
  rule and `DelegationScopeMissing` reason when a delegated scope is absent.

## Reused by CS21

CS21 (break-glass / time-boxed delegation) builds directly on the mechanism defined here rather than
duplicating it. The `Actor` contract, the constrained-delegation intersection, the
`DelegationScopeMissing` reason, and the actor-aware audit fields are the delegation primitives CS21
composes — a break-glass grant is an OBO delegation with an elevated, expiring scope set and an
audit trail that already records the acting party distinctly from the effective user.

## See also

- [PDP contract](pdp-contract.md) — the `Subject` / `Actor` request shape and the self-explaining
  decision every engine returns.
- [Audit pipeline](audit-pipeline.md) — how PDP decision events (including the actor fields) flow to
  the audit sink.
- [Keycloak realm README](../../infra/keycloak/README.md#agent--non-human-access-cs19) — the
  `bank-agent` client, its scopes, and how to obtain an agent token.
