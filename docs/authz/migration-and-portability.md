# Migration & portability

> **Scope:** how the PDP makes an authorization engine a **portable, swappable** choice and how an
> existing **RBAC** policy migrates to a **ReBAC** model without a leap of faith. It documents the
> shipped CS20 surface in `AuthzEntitlements.Authz.Pdp`: config-driven engine selection (no
> app-code change), the deterministic `RBAC → ReBAC` translator (`roles as usersets`) with an
> in-process parity resolver, and the shadow / dual-run gate that proves two engines agree before a
> swap is trusted. Builds on the [PDP contract](pdp-contract.md) and the
> [policy lifecycle](policy-lifecycle.md); the step-by-step companion for plugging in a new engine
> is [adding an engine adapter](adding-an-engine-adapter.md).

## Overview

Extensibility here means three concrete, testable things:

1. **Config-driven engine swap** — the active engine is a configuration value, not a code
   dependency. The same calling code decides through whichever engine `Pdp:Provider` names.
2. **Model translation** — an existing flat RBAC policy converts *mechanically and deterministically*
   into an OpenFGA "roles as usersets" ReBAC model, with an in-process resolver that proves the
   translation decides identically to the source RBAC.
3. **A migration safety net** — before any swap is trusted, the shadow / dual-run harness proves the
   candidate engine agrees with the incumbent across the whole scenario catalog, and it *catches* a
   genuine divergence rather than rubber-stamping one.

Everything below runs **in-process, deterministically, with no Docker and no live server**, so the
demonstrations are part of the ordinary build/test gate.

## Config-driven engine swap (no app-code change)

The active engine is resolved by name from configuration. `PdpOptions.Provider` binds the
`Pdp:Provider` key and defaults to `reference`
([`PdpOptions.cs`](../../src/AuthzEntitlements.Authz.Pdp/Providers/PdpOptions.cs)):

```json
{ "Pdp": { "Provider": "cedar" } }
```

[`AuthorizationDecisionProviderFactory`](../../src/AuthzEntitlements.Authz.Pdp/Providers/AuthorizationDecisionProviderFactory.cs)
resolves the active provider from every registered `IAuthorizationDecisionProvider`, matching
`Pdp:Provider` **case-insensitively** on each provider's `Name`. Registration happens once in
[`PdpServiceCollectionExtensions.AddPdp`](../../src/AuthzEntitlements.Authz.Pdp/Providers/PdpServiceCollectionExtensions.cs).

The portability claim is that **calling code never names an engine**. There is exactly one,
engine-agnostic call site:

```csharp
// the single call site every engine-swap case invokes — no concrete engine appears here
factory.GetActiveProvider().Evaluate(request);
```

[`EngineSwapPortabilityTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/EngineSwapPortabilityTests.cs)
proves this directly: its `DecideVia` helper is that one unchanged call site, and
`ConfigSwap_OnlyConfigChanges_AcrossEveryRbacEngine` iterates the config value alone —
`reference`, `aspnet`, `casbin`, `cedar` — asserting each time that `GetActiveProvider().Name`
equals the configured value and the decision is unchanged. The app code is byte-identical across
every engine; only the configuration string moves.

**Fail-closed selection.** A non-blank but unknown provider name never silently falls back to some
engine. `GetActiveProvider()` throws an `InvalidOperationException` that names the unknown provider
and lists the registered ones; at the request boundary the shadow / lifecycle endpoints use
`TryGetProvider` to return a `400` for an unknown engine name instead of a wrong-engine answer. A
*blank* value is the only thing that falls back — to the `reference` default — and duplicate or
blank provider `Name`s are rejected at construction, so selection is never ambiguous.

## RBAC → ReBAC translation

The textbook migration from role-based to relationship-based access control is the **"roles as
usersets"** pattern: each role becomes a userset (the set of its members), and granting a role a
permission becomes a single relationship tuple. CS20 ships that translation as a pure, deterministic
function.

### The source: a flat RBAC policy

[`RbacPolicy`](../../src/AuthzEntitlements.Authz.Pdp/Migration/RbacPolicy.cs) is a DB-free RBAC
model — roles, permissions, the permissions each role grants, and the roles each user holds. Its
fail-closed factory `RbacPolicy.Create(roles, permissions, rolePermissions, userRoles)` rejects any
dangling reference (a grant or assignment naming an undeclared role/permission) as a policy-authoring
bug rather than evaluating it to a wrong decision. `bool IsAllowed(string user, string permission)`
is classic RBAC evaluation: allowed iff some role the user holds grants the permission; an unknown
user or permission is denied.

[`FintechRbacPolicy.Policy`](../../src/AuthzEntitlements.Authz.Pdp/Migration/FintechRbacPolicy.cs) is
the concrete fintech grant matrix, mirroring the reference engine's role eligibility exactly on the
role dimension:

| Role | Granted permissions |
|---|---|
| `Teller` | `bank.transaction.create` |
| `BranchManager` | `bank.account.create`, `bank.transaction.create`, `bank.transaction.approve`, `bank.transaction.reject` |
| `ComplianceOfficer` | `bank.transaction.create`, `bank.transaction.approve`, `bank.transaction.reject` |
| `Auditor` | *(none — read-only)* |

Users: `teller-anna` (Teller), `branch-mgr-ben` (BranchManager), `compliance-cara`
(ComplianceOfficer), `auditor-dan` (Auditor), and `manager-and-compliance` (BranchManager +
ComplianceOfficer, to exercise the multi-role union).

### The translator

[`RbacToRebacTranslator.Translate(RbacPolicy)`](../../src/AuthzEntitlements.Authz.Pdp/Migration/RbacToRebacTranslator.cs)
returns a
[`TranslatedRebacGraph`](../../src/AuthzEntitlements.Authz.Pdp/Migration/TranslatedRebacGraph.cs). It
is pure and deterministic: the same policy always yields byte-identical `ModelJson` and a stably
ordered `Tuples` list (verified by `Translate_IsDeterministic_AcrossCalls`).

The emitted OpenFGA schema-1.1 model has **three types**:

- `user` — the principals.
- `role` — one relation, `assignee: [user]`, so `role:R#assignee` is the userset of R's members.
- `resource` — **one relation per RBAC permission**, each `directly_related_user_types` set to
  `role#assignee`, so granting a role a permission is a single tuple against the shared resource
  object. That object is `resource:core`, exposed as `TranslatedRebacGraph.ResourceObject` (its bare
  id half, `core`, is `TranslatedRebacGraph.ResourceObjectId`).

Permission names are sanitized into valid OpenFGA relation names — OpenFGA relation identifiers must
match `^[a-z][a-z0-9_]{0,62}$` (start with a lowercase letter, then lowercase letters / digits /
underscores, up to 63 characters). The sanitizer lowercases and collapses **every** character
outside `[a-z0-9_]` (including `.`, `:`, `#`, `@`, `/`, `-`, and whitespace) to `_` (so
`bank.transaction.create` → `bank_transaction_create`). An empty or whitespace-only permission is
rejected outright, and any sanitized result that is **not** a valid OpenFGA relation identifier —
starting with a digit or `_`, or exceeding 63 characters — is likewise rejected fail-closed with an
`ArgumentException` rather than emitted as an invalid model. The map is bidirectional
(`PermissionToRelation` / `RelationToPermission`); a name collision between two distinct permissions
is likewise a fail-closed `ArgumentException` because it would silently merge grants.

The graph carries two **tuple forms** (reusing
[`RebacTuple`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/RebacSeedTuples.cs) `(User,
Relation, Object)`):

- user → role assignment: `("user:{u}", "assignee", "role:{r}")`
- role → permission grant: `("role:{r}#assignee", "{permissionRelation}", "resource:core")`

### The in-process parity resolver

`TranslatedRebacGraph.Check(userObject, permission, resourceObject)` evaluates the roles-as-usersets
semantics **directly over the emitted tuples**, mirroring how an OpenFGA `Check` resolves: the user
has the permission on the resource iff some role R has a
`role:R#assignee → permRelation → resourceObject` grant *and* the user has a
`userObject → assignee → role:R` assignment. Unknown permission, user, or resource all resolve to
`false` (fail closed).

This resolver is what lets
[`RbacToRebacTranslatorTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/RbacToRebacTranslatorTests.cs)
prove **parity** without standing up a live OpenFGA server. The headline assertion,
`Check_MatchesRbac_AcrossFullUserPermissionGrid`, checks that for the full user × permission grid the
translated graph's `Check` matches the source RBAC's `IsAllowed` — the faithfulness proof.

### A short worked example (Teller)

`teller-anna` holds `Teller`, and `Teller` grants `bank.transaction.create`. The translation emits
these two tuples (among others):

```text
# user → role assignment
("user:teller-anna", "assignee", "role:Teller")

# role → permission grant
("role:Teller#assignee", "bank_transaction_create", "resource:core")
```

So `graph.Check("user:teller-anna", "bank.transaction.create", "resource:core")` is `true` — it
finds the Teller grant tuple, then confirms Anna's assignment into `role:Teller`. And
`graph.Check("user:teller-anna", "bank.account.create", "resource:core")` is `false`, because Teller
holds no `bank_account_create` grant. Both match `RbacPolicy.IsAllowed` exactly, which is the parity
claim the test grid enforces across every user and permission.

### Generated vs. hand-authored: where the real ReBAC value is

The translator gives you a **faithful RBAC-equivalent starting point** — a mechanical projection of
the flat role→permission matrix. It is *not* the ceiling of what ReBAC buys you. Contrast it with the
hand-authored CS07 model in
[`RebacModel`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/RebacModel.cs), which models
**relationships the flat RBAC never had**:

| Aspect | Generated (translator) | Hand-authored `RebacModel` (CS07) |
|---|---|---|
| Types | `user`, `role`, `resource` | `user`, `region`, `branch`, `customer`, `account` |
| Core idea | roles as usersets | genuine relationships |
| Relationships | user→role, role→permission | ownership (`account.owner`), RM→customer (`relationship_manager` → `can_view`), branch/region hierarchy (`branch.manager` inherits `region.manager`), delegation (`account.delegate`) |
| Derivation | one direct grant per role/permission | `can_view` / `can_transact` compose direct + derived paths via `computedUserset` / `tupleToUserset` |

The point: translation migrates the RBAC decision **losslessly** so nothing regresses on day one;
the *value* of ReBAC comes afterward, by modeling ownership, hierarchy, and delegation that a flat
role list structurally cannot express.

## Dual-run / shadow as the migration safety net

A config swap is only safe once you have **evidence** the new engine decides identically to the one
it replaces. That evidence is the shadow / dual-run comparison
([`ShadowRunner`](../../src/AuthzEntitlements.Authz.Pdp/Lifecycle/ShadowRunner.cs)).

`ShadowRunner.RunCatalog(primary, shadow, scenarios)` evaluates the **whole**
[`FintechScenarioCatalog`](../../src/AuthzEntitlements.Authz.Pdp/Catalog/FintechScenarioCatalog.cs)
(22 scenarios) through both engines and collects only the divergences. An empty divergence list
(`report.AllAgree`, `report.Agreements == report.Total`) is the **go** verdict a migration gate
checks. The same comparison is exposed over `POST /api/authz/shadow/catalog`; the single-request form
is `POST /api/authz/shadow`, which falls back to the deterministic in-process RBAC family
(`ShadowRunner.DeterministicRbacFamily` = `reference`, `aspnet`, `casbin`, `cedar`) when no shadow
engine is named. Both endpoints fail closed with a `400` on an unknown engine name.

`EngineSwapPortabilityTests.DualRun_ReferenceVsRbacEngine_HasZeroDivergences` runs exactly this gate
for `casbin`, `cedar`, and `aspnet` against `reference` and asserts zero divergences. Crucially, the
gate is **not vacuous**: `DualRun_WouldCatchDivergence_WhenAnEngineDisagrees` shadows a deliberately
drifting engine (one that flips a deny into a permit) and asserts the report surfaces both the
`decision:` and `reason:` mismatch — so a bad migration cannot slip through.

The comparison keys on decision, primary reason code, and obligations (obligations compared
order-insensitively). For the fuller lifecycle treatment — the golden snapshot, what-if simulation,
drift detection, and how shadow fits the rollout/rollback flow — see
[policy lifecycle](policy-lifecycle.md); this document does not duplicate it.

## What maps and what doesn't

The translation is faithful **on the pure-RBAC dimension only** — the role → permission relationship.
That is the part that projects mechanically into usersets and tuples.

Everything contextual is **out of the RBAC projection** by design. The fintech decision also depends
on coarse OAuth **scope** re-checks, **tenant** isolation, subject-**is-maker**, transaction
**pending** status, the maker-checker **threshold** obligation, and **segregation of duties** — all
of which are ABAC / contextual rules, not role grants. They are decided by the ABAC-capable engines
(the reference engine, Cedar, OPA) or, in a ReBAC world, expressed as relationship / contextual
tuples — never smuggled into the flat role→permission matrix. Note too that `bank.account.read` is
**scope-gated, not role-gated**, which is why it is intentionally absent from `FintechRbacPolicy`'s
permission set.

In short: translation gets you a faithful RBAC-equivalent starting point with zero regression; the
contextual rules keep living where they already do, and the richer relationship value of ReBAC is
something you *add* on top, not something the mechanical translation invents for you.

## See also

- [PDP contract](pdp-contract.md) — the `IAuthorizationDecisionProvider` seam and request/response
  shapes every engine speaks.
- [Adding an engine adapter](adding-an-engine-adapter.md) — the step-by-step guide to plugging a new
  engine into the seam described above.
- [Policy lifecycle & testing](policy-lifecycle.md) — the fuller shadow / dual-run, golden-snapshot,
  and drift-detection treatment.
