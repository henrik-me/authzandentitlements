# CONVENTIONS

> **File class: composed.** The sections below are managed by the harness and overwritten on
> every `harness sync`. Do not edit them directly — changes will be lost. Project-specific
> conventions belong exclusively in the local block at the bottom of this file.

---

## File naming

- Use **lowercase kebab-case** for all source files and directories: `my-module.mjs`,
  `user-auth/`, `check-composed-blocks.mjs`.
- Test files mirror the module they test, suffixed with `.test`: `composed.test.mjs` tests
  `lib/composed.mjs`.
- Configuration and schema files use kebab-case: `harness.config.json`,
  `harness.config.schema.json`.
- Markdown documents use `SCREAMING_SNAKE_CASE.md` for process docs (`CONVENTIONS.md`,
  `INSTRUCTIONS.md`) and kebab-case for reference docs under `docs/` (`0001-file-classes.md`).
- Place fixtures and test data next to the code or test file that uses them, not in a
  top-level `fixtures/` folder, unless multiple test files share the same fixture.
- Avoid generic names like `utils.mjs` or `helpers.mjs`; prefer names that describe what
  the module does (`format-date.mjs`, `resolve-path.mjs`).

---

## Branch naming

- **Feature / CS work:** `cs<NN>/<slug>` — e.g. `cs08/content`.
- **Workboard-only changes:** `workboard/cs<NN>-<action>` — e.g. `workboard/cs08-claim`,
  `workboard/cs08-close`.
- **Hotfixes:** `fix/<short-slug>` — squash-merged to `main` with a standard commit message.
- **Experiments:** `exp/<short-slug>` — never merged without explicit review and rename.
- Slugs are lowercase kebab-case, ≤40 characters. No personal identifiers, ticket numbers,
  or dates in the slug unless they are genuinely disambiguating.
- Branch names are stable: do not rename a branch after opening a PR against it.

---

## Commit conventions

- **Subject line:** short imperative sentence, ≤72 characters, no trailing period.
  Example: `Add composed-file merge engine`.
- **Body:** one blank line after the subject, then a paragraph explaining *why* the change
  was made. Include context that is not obvious from the diff. Wrap at 72 characters.
- **Trailers:** always include the Co-authored-by trailer on every commit made with agent
  assistance. Per CS35 Decision C35-5 the harness's PR-evidence B1 gate (lands in CS36)
  enforces this on **every commit** in `git log <base>..<head>`, NOT only on the squash
  commit — squashing hides intermediate dirty state and that is exactly what B1 catches.
  ```
  Co-authored-by: Copilot <223556219+Copilot@users.noreply.github.com>
  ```
  To satisfy this by construction — including on **merge** commits, which is the easy case
  to forget — run `npx -y github:henrik-me/agent-harness#v0.17.0 install-hooks` once to install the opt-in
  `prepare-commit-msg` hook (CS100); it appends the trailer above when the exact line is
  absent. The hook is opt-in (never auto-installed) and leaves a pre-existing hook untouched
  unless `--force`. See [OPERATIONS.md § Commit-trailer hook](OPERATIONS.md#commit-trailer-hook-install-hooks).
- **Squash-merge only** on `main`. Feature branch history is preserved locally but only
  the squash commit appears in `main`'s log.
- **No force pushes** to `main`. Force-pushing a feature branch before merge is acceptable
  but must be communicated to any co-authors.
- **Atomic commits:** each commit should leave the repo in a buildable, testable state.
  Avoid "WIP" or "fixup" commits on long-running feature branches that will be squashed.

---

## Pull request conventions

- Every PR must reference the clickstop it belongs to (e.g. `CS08`) in the title or body.
- PR title follows the same imperative-subject format as commit messages.
- The PR description includes: **What** changed, **Why** it was needed, **Testing** done,
  and any **Known limitations** or follow-up work.
- All PRs undergo a GPT-5.5 rubber-duck pre-merge review (or a documented fallback) before
  merge — see `REVIEWS.md` for the process. This requirement holds in the private phase
  except via explicit waiver.
- Keep PRs small and focused. A PR should ideally change one logical area; split large
  changes into sequenced PRs with explicit dependencies noted.
- Draft PRs are welcome for early feedback but must be converted to Ready before the
  rubber-duck review step.
- Delete the source branch after merge unless it is a long-lived integration branch.

**PR-evidence skip predicates (CS35 C35-7/8/9):**
- PRs labeled `workboard-only` skip ALL PR-evidence gates (per Decision C35-7) —
  workflow-level `if: !contains(labels.*.name, 'workboard-only')`. Used for the claim
  and close-out PRs that touch only `WORKBOARD.md` + the CS rename.
- Bot-authored PRs (`dependabot[bot]`, `github-actions[bot]`) skip the per-commit
  trailer (B1), per-file enumeration (A2), and stale-diff (A4) gates per Decision
  C35-8 (bot PRs lack the doctrine-required content by construction). The plan-review
  attestation gate (A6, CS35b) and Copilot engagement gate (A16) still apply if a
  Copilot review is explicitly requested.
- Fork PRs run all read-only gates normally; the Copilot mutation gate (CS41) cannot
  run from a fork because `GITHUB_TOKEN` is read-only on fork PRs (per Decision C35-9).
  The mutation gate fails loudly with a maintainer-rerun instruction; do not paper over.

---

## Code style fundamentals

These rules apply across all languages and tool stacks. Language-specific rules belong in
the local block below.

- **Consistency over cleverness.** Prefer readable, conventional code over terse tricks.
  A future reader — human or agent — should understand the intent without tracing context.
- **Small modules.** Target ~100 LOC per module where reasonable. Large modules are a signal
  to split by responsibility, not a rule violation, but they attract extra review scrutiny.
- **Pure functions first.** Prefer pure, side-effect-free functions. Isolate I/O and state
  mutation at the boundary (entry points, command handlers). This makes testing easier and
  logic clearer.
- **No global mutable state.** Module-level constants (frozen objects, regex literals,
  enum-like maps) are acceptable. Module-level variables that mutate at runtime are not.
- **Explicit over implicit.** Prefer explicit function parameters over hidden dependencies.
  Avoid relying on ambient globals, process environment leaks, or module-load side effects.
- **Error handling:** use structured error objects with a `code` string property for
  programmatic errors. Plain `Error` with only a message is acceptable for programmer errors
  (precondition violations). Never swallow errors silently.
- **No commented-out code** in committed files. Remove dead code; if it may be needed, note
  the intent in a comment that explains the trade-off, not the code itself.
- **Comments explain why, not what.** If a comment re-states what the code does, it is
  redundant and should be deleted. Reserve comments for non-obvious decisions, external
  constraints, or known limitations.

---

## Documentation conventions

- All process docs live at the repo root or under `template/` in the harness.
- Use H2 (`##`) for top-level sections and H3 (`###`) for subsections. Do not skip heading
  levels. Do not use H1 inside a document body (the document title is the only H1).
- Cross-links use repo-relative paths, e.g. `[the project README](README.md)`. Do not
  use absolute URLs for in-repo links.
- Code, file names, command names, and configuration keys are wrapped in backticks.
- Avoid passive voice in normative statements. "Do X" is clearer than "X should be done."
- Tables are used for comparative or tabular data only. Do not use tables as a layout trick
  for two-column prose.
- Keep line length ≤100 characters in Markdown source where practical. This is not enforced
  by CI but aids diff readability.
- ADRs follow a fixed structure: title, date, status, context, decision,
  consequences. Do not add sections outside that structure without updating this file.

---

## What goes where

| Path | Contents |
|---|---|
| Repo root | Managed and seeded process docs (`INSTRUCTIONS.md`, `CONVENTIONS.md`, etc.) |
| `template/managed/` | Harness-owned templates overwritten on every sync |
| `template/composed/` | Templates with managed core + local extension blocks |
| `template/seeded/` | Templates seeded once on init; consumer owns thereafter |
| `docs/` | Reference docs, including Architectural Decision Records (immutable once accepted) |
| `lib/` | Harness library modules (pure ESM, `.mjs`) |
| `bin/` | Harness CLI entry points |
| `scripts/` | Harness development and CI scripts |
| `schemas/` | JSON Schema files for config and lock formats |
| `scaffolds/` | Skeleton files copied during `harness init` |
| `project/clickstops/` | CS lifecycle (`planned/`, `active/`, `done/`) |

Files not listed in `harness.config.json` (under `managed`, `composed`, `seeded`, or
`excluded`) cause `harness sync` to exit non-zero. Every file the harness ships must be
accounted for.

---

## Project-specific conventions

<!-- harness:local-start id=conventions.project -->
### Language + build

- **.NET 10 / C#**, ASP.NET Core minimal APIs + .NET Aspire; EF Core 10 + Npgsql on
  Postgres. **Central Package Management**: every `<PackageVersion>` lives in
  `Directory.Packages.props`; `.csproj` `<PackageReference>` entries omit `Version`.
- `Directory.Build.props` sets **`TreatWarningsAsErrors=true`** — build **0 warnings**.
  LF line endings, no BOM (the harness text-encoding gate rejects CRLF/BOM).
- **NuGet audit under warnings-as-errors:** suppress a *specific* advisory with a per-ID
  `<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-…" />` in
  `Directory.Build.props` — never a blanket `NoWarn=NU1902;NU1903`, which would also mask
  future advisories (**LRN-002**). Better still, *remediate* rather than suppress where a
  patched version exists — pin it via CPM transitive pinning
  (`CentralPackageTransitivePinningEnabled=true` + a `<PackageVersion>` entry), which
  actually clears `dotnet list package --vulnerable` whereas suppression does not
  (**LRN-003 / LRN-005**; see `docs/security/nuget-audit-reeval-2026-07-04.md`).
- **Line endings are gated by `harness lint` (text-encoding) + `.gitattributes eol=lf`, NOT
  `dotnet format`.** The file-authoring tool writes CRLF for new `.cs` on Windows and the
  text-encoding gate does not always flag it — convert authored files explicitly
  (`[IO.File]::WriteAllText($p, ([IO.File]::ReadAllText($p) -replace "\r\n","\n"), (New-Object Text.UTF8Encoding $false))`)
  before committing. Treat
  the dotnet-profile `dotnet format --verify-no-changes` self-check as advisory until a repo
  `.editorconfig` with `end_of_line = lf` exists (LRN-036).
- **Cross-SDK project references:** `<FrameworkReference Include="Microsoft.AspNetCore.App" />`
  does **not** propagate transitively from a referenced `Sdk.Web` project to a plain
  `Microsoft.NET.Sdk` console/test project — any project that touches ASP.NET-Core types must
  declare it itself. Freeze a shared reflection-based `JsonSerializerOptions` with
  `MakeReadOnly(populateMissingResolver: true)` — the parameterless overload throws (LRN-046).

### Fail-closed authorization + entitlements (security)

Authorization and entitlement code is security-critical — apply these at **authoring**
time; they have repeatedly been review blockers (LRN-011, LRN-017):

- **Never trust caller-supplied security attributes.** Derive tenant / branch / owner /
  maker / checker from the **trusted** source — the loaded resource row or the validated
  token (`sub`, `tenant` claim) — never from the request body. A caller may not act as
  another subject; bind maker/checker/tenant to the token.
- **Fail closed on every gate.** A missing/unknown claim, an unreachable dependency, an
  unknown key, a malformed payload, or a decision-service error must **deny**, never
  allow: missing tenant claim → 403; entitlements/PDP unreachable → deny (503); unknown
  feature/module/policy key → disabled/deny **without** consulting a downstream provider
  (the local catalog is the source of truth); malformed input → deny + clear error, never
  a silent default.
- **Distinguish transient failures from business denials.** A decision endpoint returns
  **2xx allow/deny** for business outcomes and a **5xx (503)** for transient/infrastructure
  failures, so a fail-closed caller maps the 5xx to "unavailable → deny" rather than
  mislabeling it as a business decision (e.g. a quota-store retry-exhaustion is a 503, not
  a 429 "quota exceeded").
- **Defense in depth.** Token/scope/role checks are an **outer** gate over domain
  invariants (maker-checker, SoD, tenant scoping), which still enforce independently —
  never the only line of defense.
- **An override / elevation / emergency / admin-bypass control that raises a denial MUST
  re-verify the hard integrity invariants independently** — never trust the primary/first deny
  reason code. Rule evaluators check capability (scope/role) BEFORE integrity (tenant /
  maker-checker-SoD / subject-is-maker / pending) and short-circuit on the first failure, so a
  request lacking capability AND violating an integrity invariant surfaces the *elevatable*
  capability reason and MASKS the integrity violation (bypassing tenant isolation / SoD). Gate
  elevation behind an independent hard-invariant guard (`PassesHardInvariants`) that re-checks
  every integrity invariant for the action (reuse the provider's existing integrity predicates —
  no duplicated rule logic); elevation applies ONLY to a *pure* missing-capability denial. Test
  the **capability+integrity combined-failure** class, not only pure-invariant denials (the
  pure-invariant tests pass even when the control is broken) (LRN-065).
- **A retention / eviction / GC policy on a store that ALSO backs a mandatory-follow-up control
  must rank items still pending that follow-up as LEAST evictable**, never "terminal". The
  break-glass grant store evicts by `EvictionRank`: `reviewed` (evict first) → `still-active` →
  `expired-but-unreviewed` (evict LAST) — because expired-but-unreviewed grants ARE the
  pending-review queue the mandatory-post-review control depends on, so a terminal-first policy
  would silently drop a grant still owing its review (and its audit-correlation id). Before adding
  retention to any store, enumerate every downstream control that reads it and confirm eviction
  never drops an item a control still needs (mandatory-review / audit-completion queues) (LRN-066).
- **Client sentinels are non-deserializable.** Fields that signal a *local* fail-closed
  state on a typed-client result (e.g. `IsUnavailable`, a sentinel `Reason`) must be
  `[JsonIgnore]` so a wire payload can never inject them; only the local `Unavailable(…)`
  factory sets them.
- **Emit audit-ready decision events.** Every authz/entitlement decision emits a structured
  event with stable, matchable fields — the decision-type/outcome **values** are lower-cased
  for stable matching; ingestion may be deferred (Audit.Service, CS13) but emission is not.
- **A self-skipping live-probe / evidence tool classifies THREE cases, never two:** (a) transport
  failure / timeout → self-skip (`collected=false`); (b) service REACHED but non-success HTTP
  status OR malformed body → **fail closed** (clear error + non-zero exit); (c) genuine caller
  cancellation → **propagate** `OperationCanceledException`. An HttpClient timeout and a caller
  cancellation both surface as `OperationCanceledException`, so separate them —
  `catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)` → self-skip
  (timeout), and let a genuinely-cancelled caller token propagate. Collapsing (b) into (a) hides a
  broken service behind an all-clear — a fail-OPEN evidence gap (LRN-050).
- **On-behalf-of (OBO) / delegation resolves ONLY for an ordinal (case-sensitive) allow-list of
  recognized delegate kinds** matching the PDP `Actor.Type` domain (`{agent, service}`); a
  blank / unknown / mis-cased `subject_type` (`robot`, `AGENT`, `Service`) with a non-blank
  `on_behalf_of` must NOT resolve as a delegation ("any non-human" is fail-OPEN). The
  constrained-delegation decision is the **intersection** (user-permit ∧ agent-delegated-scope),
  so an agent never exceeds the user; a base user-Deny and the `Actor==null` direct path are
  unchanged (LRN-058).
- **Delegation / act-on-behalf-of authorization must bound the delegate by the intersection of
  the delegate's own token capability AND the delegator's granted scopes** — the grant is
  authoritative for what was actually delegated (the least-privilege ceiling), not just the
  delegate's asserted token scopes. The PDP `DelegationGrant` carries the delegated `Scopes`; the
  action's required scope must be present in BOTH `Actor.Scopes` AND the grant's `Scopes`
  (fail-closed on either), so a delegate holding broader token scopes can never exceed what the
  delegator actually delegated (LRN-067, extends LRN-058).
- **Sanitize CR/LF from EVERY request-/engine-derived string rendered into an `ILogger` message**
  — even via structured `{Placeholder}` args (the default renderer substitutes values into the
  text, so CodeQL `cs/log-forging` / CWE-117 does not treat structured logging as a barrier). Use
  `AuthzEntitlements.ServiceDefaults.LogSanitizer.Clean(...)` on every rendered string, including
  joined collections (OpenFGA `PolicyReferences` embed caller-derived ids); the hash-chained
  audit-of-record keeps raw values — only the human-readable log string is sanitized. The
  `code_scanning` ruleset blocks merge on open high+ alerts — fix, don't dismiss (LRN-059; applied
  repo-wide by CS34).

### Concurrency (Postgres + EF Core)

- **Hard capacity caps use a pessimistic per-subject lock, not `Serializable` + retry.** For a
  `count → check → insert` capacity cap (seats today; and new grant/capacity work such as JIT
  grants) enforce it atomically with a Postgres **advisory transaction lock** —
  `SELECT pg_advisory_xact_lock(hashtextextended(<id>, 0))` issued inside the EF transaction
  via `db.Database.ExecuteSqlInterpolatedAsync(…)`. It serializes (blocks) rather than
  conflict-retrying, so it does not thrash/500 under contention; a `Serializable`-isolation +
  retry loop exhausts and 500s (LRN-015, verified to 30-way concurrency on seat assignment).
- **Decide-once / last-writer races use `xmin` optimistic concurrency.** Map the Postgres
  system column with `entity.Property<uint>("xmin").IsRowVersion()`
  (`UseXminAsConcurrencyToken()` was removed in Npgsql 10 rc1); verify the generated SQL adds
  no physical column (LRN-004). **Approve/reject** (maker-checker decide-once) surfaces a
  **409** on the losing writer instead of last-writer-wins. **Quota-consume** instead uses an
  `xmin` **retry** loop (re-read + re-evaluate) and, on sustained-contention retry exhaustion,
  fails closed with a transient **503** (never a 200 business deny or a 429), per the
  fail-closed convention above (LRN-017).

### Architecture Decision Records (ADRs)

Project ADRs under `docs/adr/` **extend** the base ADR structure (title, date, status,
context, decision, consequences) with two additional H2 sections, placed after
`## Consequences` in this order: **`## Alternatives considered`** and
**`## When to use / when not`**. The when-to-use section carries the CS23 "when to use"
guidance per engine/decision; the companion evidence lives in the comparison matrix
(`docs/eval/comparison-matrix.md`) and market survey (`docs/eval/market-survey.md`). ADRs
are **retrospective** where they formalize an already-shipped decision — each records the
realizing clickstop in its metadata line (`Realized in: CS<NN>`).

### Aspire + project scaffolding

- **Add Aspire integrations with `dotnet add package Aspire.Hosting.<X>`**, not `aspire add`
  — the Aspire CLI aborts (exit 5, "Interactive input is not supported") in a
  non-interactive agent shell even with `--non-interactive`. The `aspire-apphost` template
  emits `AppHost.cs` (not `Program.cs`); normalize `dotnet new` CRLF output to LF/no-BOM for
  the text-encoding gate (LRN-001).
- **Adding an Aspire service edits BOTH `AppHost.cs` AND `AppHost.csproj`** — the AppHost SDK
  source-generates the `Projects.<Name>` type from the csproj `<ProjectReference>`, so
  `AppHost.cs` cannot compile without it. A sub-agent adding a service must own
  `AppHost.csproj` (and, for a brand-new project, the `.sln` + `Directory.Packages.props`)
  alongside `AppHost.cs` (LRN-016).

### Keycloak, OIDC & JWT wiring

- **Keycloak 26 realm import with a service-account client** crashes on the default-on
  `organization` feature — disable it with `KC_FEATURES_DISABLED=organization`. Aspire's
  `WithRealmImport` enforces the `<realm>-realm.json` filename convention (name the file
  `authz-bank-realm.json`, not `authz-realm.json`) (LRN-008).
- **Pin Keycloak to a fixed host port (`AddKeycloak(name, port)`) and inject ONE explicit
  `Keycloak:Authority`** shared by every service and the browser — a dynamic/proxied Aspire
  endpoint stamps a different `iss` per access path, breaking JWT issuer validation (LRN-009).
- **Set `JwtBearerOptions.MapInboundClaims=false` for custom claim names** (e.g. Keycloak's
  top-level `roles`) — the default `true` remaps `roles`→legacy `ClaimTypes.Role` (and
  `sub`→nameidentifier), silently breaking `RequireRole`/`IsInRole`. Synthetic-principal unit
  tests bypass the JWT handler and never catch this — add an options-level regression test
  (LRN-010).
- **A hand-authored realm export that supplies its own `clientScopes`** does not auto-seed
  the built-in `basic`/`profile`/`email`/`roles` scopes, so `sub` (moved to `basic` in
  KC 24+), `preferred_username`, and `email` go missing and requesting undefined scopes fails
  the PAR authorization with "Invalid scopes". Carry the required OIDC claims via a custom
  applied default scope and request only defined client scopes (LRN-012).

### Blazor Web App (token-protected UI)

For a .NET-10 Blazor Web App that calls token-protected APIs (LRN-048):

- Token-forwarding pages MUST be **static SSR** (no `@rendermode InteractiveServer`) so
  `IHttpContextAccessor.HttpContext` / `GetTokenAsync` are available on the request; an
  interactive component reads identity from the cascaded **`AuthenticationState`**, not
  `IHttpContextAccessor` (a circuit has no per-event `HttpContext`).
- `app.MapStaticAssets()` is **required before `MapRazorComponents`**, or
  `_framework/blazor.web.js` (and `wwwroot/*`) 404 and interactive islands never hydrate.
- Under warnings-as-errors: **BL0008** forbids a property initializer on a
  `[SupplyParameterFromForm]` property (use `= default!` + `??= new()` in `OnInitialized`);
  **CS0542** fires when a member (e.g. an `@inject` field) is named the same as the generated
  component class — which is the `.razor` file name (so `Audit.razor` injecting
  `IAuditClient Audit` collides), not merely the `@page` route; name injected members distinctly
  (`AuditApi` / `XClient`) (LRN-048, LRN-054); `@rendermode InteractiveServer` shorthand needs
  `@using static Microsoft.AspNetCore.Components.Web.RenderMode`; multiple static-SSR
  `<EditForm>` on one page each need a unique `FormName`.

### PDP engine adapters (parity, fail-closed, reason codes)

Engine adapters are compared against the CS05 reference provider via the shared scenario
catalog (`ScenarioCatalogRunner`), which passes a provider only when it returns the exact
`Decision` AND primary reason code (`Reasons[0].Code`) in the reference's per-action order:

- **Replicate the reference's ordered checks** (scope → role → subject-is-maker → tenant →
  pending → SoD), not just the predicate *set* — combined-failure scenarios diverge on the
  reason code otherwise (LRN-021).
- **Factor RBAC-only engines** (ASP.NET Core policies, Casbin) so a shared
  `FintechRuleEvaluator` owns per-action ordering + ABAC + obligations and delegates ONLY
  role eligibility via `IEngineRoleAuthorizer`; let richer engines (OPA/Rego, Cedar) own the
  FULL decision natively rather than forcing the role-gate split (LRN-026).
- **Out-of-process adapters fail closed on ANY engine error:** `catch` (never throw), return
  a Deny with a provider-local reason code and a **stable, non-sensitive** message (log the
  cause; never surface network/config detail to anonymous callers), and wrap the WHOLE request
  flow — a singleton bootstrapped once makes "only the first call fails" reasoning wrong.
  Validate the engine's returned reason code against the bounded `ReasonCodes` vocabulary
  (fail closed on unknown). Sync `HttpClient.Send` works through ServiceDefaults'
  `AddStandardResilienceHandler` on .NET 10, so no `.GetAwaiter().GetResult()` is needed
  (LRN-027, LRN-030).
- **Declarative engines (Cedar):** build the `PolicySet` from explicit `Policy(source, id)`
  objects (not `ParsePolicies`, which renumbers ids) so `GetReason()` yields stable, semantic
  ids; model each action as a broad `permit` + one annotated `forbid` per deny reason and
  select the lowest-`Precedence` determining forbid to reproduce the reference's first-failing
  order for ANY input (LRN-032).
- **A new UNTRUSTED wire boundary** (e.g. AuthZEN `/evaluation`) that reuses a lenient internal
  mapper is fail-**open**: a present-but-unparseable `amount`→null→$0 bypasses the threshold; a
  missing `maker_id`→SoD self-approval bypass. Add action-aware fail-closed input validation
  BEFORE evaluation; harden the boundary, do not tighten the shared reference provider
  (LRN-034).
- **Telemetry test isolation:** the PDP `ActivitySource`/`Meter`/`Counter` are process-wide
  statics — isolate a metric-counter assertion by an untransformed tag (register a stub
  provider with a `Guid`-based `Name` and filter on the `provider` tag), never the normalized
  metric `action` (bounded to a known-verb/`unknown` vocabulary); span tests may isolate by the
  raw `pdp.action` tag (LRN-022).
- **Rego authoring/testing:** run `opa fmt -w infra/opa/policy` BEFORE any `opa fmt --list`
  gate; validate edits with the standalone `opa_windows_amd64.exe` download (runs with no
  install/PATH change; delete after) rather than the mocked C# adapter tests. Avoid C# raw
  *interpolated* strings with trailing braces (`$$"""…}}"""` → CS9007) for JSON test fixtures
  — use concatenation (LRN-029, LRN-037).

### Dev observability & async channels

- **Anonymous-Editor Grafana kiosk** (`grafana/otel-lgtm`) needs the anonymous settings PLUS
  the default-admin auth paths closed: `GF_AUTH_ANONYMOUS_ENABLED=true` +
  `GF_AUTH_ANONYMOUS_ORG_ROLE=Editor` + `GF_AUTH_DISABLE_LOGIN_FORM=true` +
  `GF_AUTH_BASIC_ENABLED=false` — disabling the login form alone still leaves
  `curl -u admin:admin` Basic Auth open. Model non-UI ingest ports (OTLP 4317/4318) as `tcp`
  so `WithExternalHttpEndpoints()` marks only the Grafana UI external (LRN-024).
- **Non-blocking, drop-counting `Channel<T>` producer:** use `BoundedChannelFullMode.Wait` +
  `TryWrite` (returns `false` when the buffer is full → an observable, countable drop), NOT
  `DropWrite` (under which `TryWrite` always returns `true` and drops silently) (LRN-041).
- **OTLP→Prometheus metric-name mangling is deterministic** — dots→underscores, unit suffix
  appended, `_total` on counters (`pdp.decisions.total`→`pdp_decisions_total`;
  `pdp.evaluate.duration` ms→`pdp_evaluate_duration_milliseconds_bucket`). Derive a new Grafana
  panel's Prometheus metric name from the C# `Meter`/instrument name by this rule, but treat a
  panel over a never-yet-scraped custom metric as **inference** until confirmed against a live
  `/metrics` scrape (`aspire run` + exercise the decision path); prefer an already-scrape-proven
  metric for flagship panels (LRN-051).

### Eval-lab economics docs

- **Author economics / TCO docs at the pricing-MODEL + cost-driver level** (what you are metered
  on — per-MAU / per-request / per-tuple / per-seat / flat / custom — and who carries which ops
  burden), NOT at the exact-figure level: list prices / tier limits / SLA %s are volatile and
  mostly not first-party (enterprise tiers are quote-only). Anchor every quantitative claim to a
  dated `## Sources` section, mark each figure indicative, and add a prominent "read for the
  model, not the number" honesty caveat. Repo-grounded claims are still fact-checked against the
  shipped surface (a five-vs-six Postgres-DB miscount was the one error caught pre-merge)
  (LRN-064).
<!-- harness:local-end id=conventions.project -->
