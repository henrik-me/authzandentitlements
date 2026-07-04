# Adapter test seams and degenerate-input parity

> **Scope:** how the PDP keeps engine adapters honest under two conditions the realistic
> 22-scenario catalog does not cover — an **out-of-process engine's permit/deny explanation**
> (OpenFGA, behind a live server) and **degenerate (blank) attribute input** — plus the
> OpenFGA authorization-model **pin + targeted tuple reconciliation** that keeps a persistent
> store from growing a new model version on every boot. Read the
> [PDP contract](pdp-contract.md) and [explainability](explainability.md) first; this note
> covers the CS31 test seams, not a new decision behaviour (the decisions are additive-only).

## The `IOpenFgaCheckClient` seam (offline explanation tests)

[`OpenFgaProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/OpenFgaProvider.cs)
maps an `AccessRequest` to a single OpenFGA forward *Check* and returns a self-explaining
permit/deny (`engine = openfga`, `DeterminingRule = relationship`, a relationship-tuple
`PolicyReference` like `user:teller1#can_view@account:acme-checking`). Historically that
permit/deny path could only be exercised against a **live** OpenFGA server, so the offline
suite could assert the pure mapper and the fail-closed boundary, but not the actual
permit/deny explanation.

CS31 extracts the narrow forward-Check seam
[`IOpenFgaCheckClient`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/IOpenFgaCheckClient.cs):

- It exposes exactly the one member the provider needs — `CheckAsync(user, relation, object)`.
- [`OpenFgaRebacService`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/OpenFgaRebacService.cs)
  implements it (still `sealed`); the reverse-index queries stay on the concrete service, which
  [`RebacEndpoints`](../../src/AuthzEntitlements.Authz.Pdp/Endpoints/RebacEndpoints.cs) inject.
- `OpenFgaProvider` depends on the interface, and DI registers the interface as the **same**
  singleton service (`sp => sp.GetRequiredService<OpenFgaRebacService>()`) so the provider and
  the endpoints share one bootstrap
  ([`PdpServiceCollectionExtensions`](../../src/AuthzEntitlements.Authz.Pdp/Providers/PdpServiceCollectionExtensions.cs)).

A test-double `IOpenFgaCheckClient` then forces `allowed = true` / `allowed = false` (or a
thrown engine error) with **no server**, so
[`OpenFgaProviderSeamTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/OpenFgaProviderSeamTests.cs)
assert the permit/deny `Decision` + `DecisionExplanation` offline, that the provider forwards
the mapped `(user, relation, object)`, that a thrown Check **fails closed** (deny
`EngineUnavailable`, never a throw), and that a mapper-level boundary deny never reaches the
engine at all (zero Check calls). The double lives in
[`FakeOpenFgaCheckClient`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/FakeOpenFgaCheckClient.cs).

## Degenerate-input fail-closed parity (vs the reference oracle)

The shared
[`FintechScenarioCatalog`](../../src/AuthzEntitlements.Authz.Pdp/Catalog/FintechScenarioCatalog.cs)
uses only well-formed, non-blank attributes, so the fail-closed predicates (tenant, maker,
status, scope) are never exercised on `null` / empty / whitespace input. A real fail-**open**
(an engine treating `"" == ""` as a tenant match) could stay green over that catalog.

[`DegenerateInputParityTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/DegenerateInputParityTests.cs)
close that gap. Each case isolates **one** fail-closed predicate (every other attribute is
valid) and asserts equivalence to the
[`ReferenceDecisionProvider`](../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs)
**oracle** — `Decision` + primary reason code — not a hardcoded expectation:

- Every in-process engine (`reference`, `aspnet`, `casbin`, `cedar`) is compared against the
  live oracle across the full case set (all four run the same ordered fail-closed pipeline —
  the reference and the shared
  [`FintechRuleEvaluator`](../../src/AuthzEntitlements.Authz.Pdp/Providers/Adapters/FintechRuleEvaluator.cs)
  both treat a blank tenant/maker/status as a fail-closed deny).
- A separate oracle-guard theory asserts the reference itself **denies** each degenerate case
  with the expected reason, so a reference regression fails at the source rather than being
  masked by every engine agreeing on a wrong permit.
- Out-of-process engines are held to their pure/mapper-level fail-closed behaviour: the OpenFGA
  [request mapper](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/OpenFgaRequestMapper.cs)
  denies a blank resource id (`MissingResourceId`) before any Check, and the
  [`opa`](opa-adapter.md) adapter fails closed (`ProviderUnavailable`) on an undefined result —
  never a permit. OPA's own degenerate ABAC parity lives in the Rego suite
  (`infra/opa/policy/authz_test.rego`).

These are kept as **separate tests**, not extra catalog rows, so the shared catalog — and the
golden / shadow / portability suites that snapshot it — stay unchanged.

## OpenFGA model-id pin + targeted tuple reconciliation

On a persistent, shared OpenFGA store, writing the embedded authorization model on every boot
accrues a new immutable model **version** each time. CS31 adds an optional pin to
[`OpenFgaOptions`](../../src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/OpenFgaOptions.cs):

- `AuthorizationModelId` (bound from `Pdp:OpenFga:AuthorizationModelId`). When set, bootstrap
  **pins** that existing version — sets it on the client and skips the write. Unset (the
  default) preserves the original **write-then-pin**: write the embedded model, pin the id the
  server returns. The pin decision is the pure static `OpenFgaRebacService.ResolvePinnedModelId`,
  unit-tested offline in
  [`OpenFgaModelPinTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/OpenFgaModelPinTests.cs).
- **Fail-safe:** a blank/whitespace id is treated as unset, so a misconfigured empty string
  never pins a bogus id — it falls back to write-then-pin.
- **Targeted reconciliation:** seed-tuple bootstrap now probes each tuple by its exact
  `(user, relation, object)` key (a fully-specified `Read`) instead of paging the entire store
  into memory, so it stays `O(seed)` small reads on a large shared store.

The live "no new model version is written" behaviour is asserted by the self-skipping
[`OpenFgaRebacIntegrationTests`](../../tests/AuthzEntitlements.Authz.Pdp.Tests/OpenFgaRebacIntegrationTests.cs)
(runs only when `OPENFGA_TEST_API_URL` is set; a soft `return` skips it offline).

## See also

- [PDP contract](pdp-contract.md) — the shared `subject / action / resource / context` shape.
- [Explainability](explainability.md) — the `DecisionExplanation` every engine attaches.
- [Adding an engine adapter](adding-an-engine-adapter.md) — the adapter/parity playbook.
- [Cedar adapter](cedar-adapter.md) and [OPA adapter](opa-adapter.md) — the in-process vs
  out-of-process engines the degenerate parity spans.
