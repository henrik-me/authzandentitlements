# AuthZEN

*OpenID AuthZEN — the vendor-neutral authorization API — and how this repository aligns with it.*

This document covers the OpenID Foundation **AuthZEN** standard, its Authorization API, and a
precise mapping from this repository's shipped PDP contract
([`IAuthorizationDecisionProvider`](../../authz/pdp-contract.md)) onto the AuthZEN request/response
shape — including where the two align and where they deliberately differ.

For the engine comparison see [../comparison-matrix.md](../comparison-matrix.md); for the survey
taxonomy see [../market-survey.md](../market-survey.md); for the full contract this repo ships see
[the PDP contract](../../authz/pdp-contract.md).

---

## What AuthZEN is

**AuthZEN** is the authorization standardization effort of the **OpenID Foundation**, produced by
the **OpenID AuthZEN Working Group**. Its goal is *interoperability of authorization*: a
vendor-neutral, engine-agnostic protocol between a **Policy Enforcement Point (PEP)** — the code
that intercepts a request — and a **Policy Decision Point (PDP)** — the engine that decides. With a
standard PEP↔PDP contract, an application can swap authorization engines (OPA, Cedar, OpenFGA,
Keycloak, a custom engine) without rewriting enforcement code, and engines can be mixed behind one
interface.

This is exactly the decoupling this repository is built around: one question shape in, one
self-explaining decision out, engines selected by configuration.

### The Authorization API

The core deliverable is the **Authorization API** and its central **Access Evaluation API**
(the single-decision "evaluation" endpoint). The request/response model is:

- **Request** — four blocks: **subject** (who is asking), **action** (what they want to do),
  **resource** (what is being acted on), and **context** (request-time environmental facts).
- **Response** — a boolean **decision** (permit/deny; the wire field is named `decision`),
  optionally accompanied by a **context** object carrying additional information such as reasons
  and advice/obligations.

A representative single evaluation is `POST /access/v1/evaluation`, with a JSON body of
`subject` / `action` / `resource` / `context` and a JSON response of `{ "decision": true|false, ... }`.

### Beyond a single decision

The Authorization API also defines:

- an **Access Evaluations API** (batch) — evaluate multiple `subject/action/resource/context`
  requests in one call; and
- **Search APIs** — discover the permitted *subjects*, *actions*, or *resources* ("what can Alice
  do?", "who can access this document?").

### Specification status

The AuthZEN **Authorization API 1.0** has been approved by the OpenID Foundation membership as an
**OpenID Final Specification** — a stable release with intellectual-property protections for
implementers and not subject to further breaking revision. Early adopters (for example Keycloak's
experimental AuthZEN support) demonstrate the interoperability goal in practice. Confirm the exact
current revision against the primary sources below before implementing against a specific field.

---

## How this repository aligns

This repository's PDP is **AuthZEN-aligned by design**. The contract an engine adapter implements —
[`IAuthorizationDecisionProvider`](../../../src/AuthzEntitlements.Authz.Pdp/Contracts/IAuthorizationDecisionProvider.cs)
— answers exactly the AuthZEN question ("*may this subject perform this action on this resource in
this context?*") and returns a self-explaining decision. See the
[PDP contract](../../authz/pdp-contract.md) for the full documentation.

### Request mapping — AuthZEN → repo

The repo's
[`AccessRequest`](../../../src/AuthzEntitlements.Authz.Pdp/Contracts/AccessRequest.cs) is four records
that line up one-to-one with the AuthZEN request blocks:

| AuthZEN block | Repo type | Notes |
|---|---|---|
| `subject` | [`Subject`](../../../src/AuthzEntitlements.Authz.Pdp/Contracts/Subject.cs) | `Type` (e.g. `"user"`) + `Id`, plus first-class fintech attributes `Roles`, `Tenant`, `Branch`, and an optional `Actor` (on-behalf-of / delegation). |
| `action` | [`ActionRequest`](../../../src/AuthzEntitlements.Authz.Pdp/Contracts/ActionRequest.cs) | Named `ActionRequest` to avoid clashing with `System.Action`, but **bound to the JSON field `action`** — so the wire name matches AuthZEN. Carries a single `Name` verb (e.g. `bank.transaction.create`). |
| `resource` | [`Resource`](../../../src/AuthzEntitlements.Authz.Pdp/Contracts/Resource.cs) | `Type` + optional `Id`, plus domain attributes (`Tenant`, `Branch`, `Amount`, `MakerId`, `Status`) modelled as typed members rather than a loose property bag. |
| `context` | [`EvaluationContext`](../../../src/AuthzEntitlements.Authz.Pdp/Contracts/EvaluationContext.cs) | Bound to the JSON field `context`; carries the coarse OAuth `Scopes` the PDP re-checks as defence in depth. |

On the wire the request is served at `POST /api/authz/evaluate` with camelCase JSON — the
`subject` / `action` / `resource` / `context` envelope is structurally the AuthZEN request shape.

### Response mapping — AuthZEN → repo

The repo's
[`AccessDecision`](../../../src/AuthzEntitlements.Authz.Pdp/Contracts/AccessDecision.cs) mirrors the
AuthZEN response, adding first-class explainability:

| AuthZEN response element | Repo type | Notes |
|---|---|---|
| `decision` (boolean) | [`Decision`](../../../src/AuthzEntitlements.Authz.Pdp/Contracts/Decision.cs) enum | `Permit` / `Deny`, with **`Deny = 0`** so the zero value **fails closed**. Modelled as an enum to leave room for a future `NotApplicable`. |
| context / reasons | `Reasons` (`IReadOnlyList<Reason>`) | Machine-stable `Code` + human `Message`; `Reasons[0]` is the primary reason and every decision carries at least one. |
| `context` (advice / obligations) | `Obligations` (`IReadOnlyList<Obligation>`) | AuthZEN 1.0 conveys advice/obligations inside the optional response `context`, not a distinct top-level field; the repo surfaces them as a first-class `Obligations` list — post-decision requirements honoured on a permit (e.g. `require_approval`), empty on deny. |
| *(extension)* | `Explanation` (`DecisionExplanation?`) | An engine-agnostic "why" (determining rule + engine-native policy references + narrative). This is a **repo extension** beyond the base AuthZEN response; the service guarantees a baseline so no decision is ever unexplained. |

### Where it aligns

- **Same question shape.** `subject / action / resource / context` in — identical to AuthZEN,
  including the JSON field names `action` and `context`.
- **Same answer shape.** A boolean decision plus supplementary information (reasons,
  advice/obligations) — AuthZEN carries the latter in the response `context`, while the repo
  elevates reasons and obligations to first-class typed fields.
- **Engine-neutral seam.** `IAuthorizationDecisionProvider` selected by name lets Casbin, OpenFGA,
  OPA, or Cedar adapters swap by configuration — the interoperability AuthZEN targets, realized at
  the in-process interface level.
- **Fail-closed by construction.** `Deny = 0` matches the defensive posture a standardized deny
  should have.

### Where it differs (be precise)

- **Naming.** The repo uses `Permit`/`Deny` (an enum) where AuthZEN's wire field is the boolean
  `decision`; the repo record is `ActionRequest` (JSON `action`) to dodge a .NET name clash. These are
  representation differences, not semantic ones.
- **Typed, domain-specific attributes.** AuthZEN models subject/resource/context as open
  property-bags (`properties`). This repo models the fintech attributes it needs
  (`Roles`, `Tenant`, `Branch`, `Amount`, `MakerId`, `Status`, `Scopes`) as **first-class typed
  members**. That is more ergonomic and safer for this domain, but a full AuthZEN gateway would map
  those typed members to/from AuthZEN's generic `properties` bags.
- **Transport / endpoint.** The repo serves `POST /api/authz/evaluate`, not the AuthZEN-canonical
  `POST /access/v1/evaluation` path. The *shape* matches; the *route* is repo-local.
- **Batch and search.** The repo contract is single-decision (`Evaluate(AccessRequest)`). AuthZEN's
  **Access Evaluations (batch)** and **Search** APIs are not part of the current contract — they
  would be additive surfaces, not changes to the decision shape.
- **Explanation extension.** `DecisionExplanation` is richer than the base AuthZEN response
  requires; it is additive and does not conflict with AuthZEN semantics.

### Net

The repository is **structurally AuthZEN-aligned** at the request/response and engine-swap level,
with intentional differences confined to (a) .NET-idiomatic naming, (b) typed domain attributes in
place of generic property bags, (c) a repo-local route, and (d) the omission of the batch/search
endpoints. None of these change the core AuthZEN semantics; a thin adapter could expose the repo's
PDP over the canonical AuthZEN endpoint without altering the decision logic.

## Sources

- OpenID AuthZEN Working Group: <https://openid.net/wg/authzen/>
- Authorization API 1.0 — Final Specification announcement:
  <https://openid.net/authorization-api-1-0-final-specification-approved/>
- Authorization API specification (canonical): <https://openid.net/specs/authorization-api-1_0.html>
- AuthZEN spec source / examples (GitHub): <https://github.com/openid/authzen>
- Repo PDP contract (this repository): [../../authz/pdp-contract.md](../../authz/pdp-contract.md)

> The AuthZEN specification evolves; verify the current revision, endpoint paths, and field names
> against the primary OpenID sources above. The repo-alignment mapping is grounded in the source
> files cited inline and in [`pdp-contract.md`](../../authz/pdp-contract.md), and is authoritative
> for this repository.
