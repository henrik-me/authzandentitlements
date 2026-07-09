# CS68 — E2E test for sign-in return-URL (+ shared OIDC-login helper)

**Status:** planned
**Owner:** —
**Branch:** —
**Started:** —
**Closed:** —
**Filed by:** omni-ae-c2 on 2026-07-08 — maintainer request: add an e2e test case validating the CS65 sign-in return-URL behavior (after a token expiry on a page, signing in returns you to that page). CS65's in-process tests cover the sanitizer, the `/login` endpoint, and link rendering, but no test drives the **real interactive OIDC flow** end to end to prove the `returnUrl` survives the Keycloak round-trip.
**Depends on:** none (builds on CS65 return-URL behavior + the CS57/58/60 e2e stack; independent of other in-flight CSs)

## Goal

Add a real-Aspire-stack e2e test that logs in through the actual Keycloak browser flow starting at `/login?returnUrl=/accounts` and asserts the user **lands on `/accounts`** (not home) — proving CS65's `returnUrl` round-trips through the OIDC authorization-code state. Reuse the proven headless-OIDC-login flow via a shared helper rather than duplicating it.

## Background

- **CS65 behavior.** `Bank.Web`'s `/login` now uses a validated, local `returnUrl` as the OIDC challenge `RedirectUri` (was hardcoded `/`). CS65's tests are **in-process** (`WebApplicationFactory`): `LoginReturnUrlTests` (sanitizer vectors), `LoginEndpointReturnUrlTests` (the `/login` challenge `RedirectUri` via an `IAuthenticationService` decorator), and the link-rendering tests. None boot the real stack or exercise the **actual** Keycloak authorization-code round-trip that carries the `RedirectUri` in the OIDC state.
- **A proven interactive-OIDC e2e already exists.** `tests/AuthzEntitlements.E2E.Tests/ApprovalsAntiforgeryE2ETests.cs` (CS60) logs in through the **real** Keycloak login form → bank-web cookie via a headless authorization-code (`response_mode=form_post`) driver (`LoginViaOidcAsync`): a hand-managed cookie jar (`UseCookies=false` — .NET's `CookieContainer` dropped Keycloak's `AUTH_SESSION_ID`/`KC_RESTART` over plain-HTTP dev), manual redirect-following, a Keycloak-login-form POST, and a self-submitting `form_post` callback replay to `/signin-oidc`. It pins Keycloak to host port 8088 (`DcpPublisher:RandomizePorts=false`) so the issuer/JWKS/authority align. That flow is exactly what a return-URL e2e needs — but it currently starts at a hardcoded `/login`, returns `void`, and its helpers are `private` to that one test.
- **Sequential boot is already enforced.** `E2ECollectionBehavior.cs` disables assembly-level test parallelization (CS58) because every e2e boots the full stack and pins 8088; a new full-stack e2e runs sequentially with the others with no extra coordination.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Reuse vs. duplicate the OIDC-login flow | **Extract** the headless OIDC-login flow (cookie jar `SendAsync`/`CaptureCookies`, redirect-follow, Keycloak-form POST, `form_post` callback replay, form/redirect parsers) from `ApprovalsAntiforgeryE2ETests` into a new shared `E2EOidcLogin` helper; **parametrize the start path** and **return the final landing `Uri`**; refactor `ApprovalsAntiforgeryE2ETests` to call it (`start="/login"`, ignores the returned Uri) | The flow is ~100 lines of fiddly logic with a subtle Keycloak-cookie fix; one copy means a future fix applies to both. Duplicating it into the new test would drift. The refactor is behavior-preserving and validated by re-running the full e2e suite. |
| 2 | What the new test asserts | `E2EOidcLogin.LoginAsync` returns the URI of the **final non-redirect app page** (after replaying the `/signin-oidc` `form_post` and following the OIDC handler's `RedirectUri` 302). Starting at `/login?returnUrl=/accounts` as `teller1`, assert that landing `Uri.AbsolutePath == "/accounts"`, then `GET /accounts` with the session cookie returns **200** and renders a **specific seeded account row** (e.g. account number `CONTOSO-CHK-0001` / customer `Alice Anderson` — verify the exact seeded value at implementation), not generic content or a count | The pre-CS65 hardcoded `RedirectUri="/"` would land on home; landing on `/accounts` with a specific seeded row proves the `returnUrl` round-tripped through the OIDC state and the user is authenticated there. Asserting a seeded-specific row (not a count) is robust. |
| 3 | Return-URL target page | Use **`/accounts`** (the Accounts list; `[Authorize]`, renders for `teller1`, seeded) | Distinct from home, renders authenticated content, and is already exercised by the existing e2e — a realistic "the page I was on" target. |
| 4 | Boot / port discipline | Reuse `E2EStack.CreateBuilderAsync` + `DcpPublisher:RandomizePorts=false` (pin Keycloak 8088) + the existing assembly-level `DisableTestParallelization` | Matches every other authenticated e2e so the issuer/JWKS/authority align and full-stack boots never run concurrently. |

## Deliverables

- **`tests/AuthzEntitlements.E2E.Tests/E2EOidcLogin.cs`** (new) — an `internal static` helper exposing the headless OIDC-browser-login flow: `LoginAsync(HttpClient, jar, bankWebBase, startPath, username, password, ct, log?) -> Uri` — drives the flow from `startPath` and **returns the URI of the final non-redirect app page** (after the Keycloak login-form POST, the `/signin-oidc` `form_post` callback replay, and the OIDC handler's `RedirectUri` 302) — plus the shared `SendAsync`/cookie-jar and the login/redirect/`form_post`-callback parsers moved from `ApprovalsAntiforgeryE2ETests`.
- **`tests/AuthzEntitlements.E2E.Tests/ApprovalsAntiforgeryE2ETests.cs`** — refactor to use `E2EOidcLogin` (replace `LoginViaOidcAsync` + the moved `SendAsync`/parsers with calls to the shared helper; keep the approve-form-specific helpers). **Behavior unchanged.**
- **`tests/AuthzEntitlements.E2E.Tests/SignInReturnUrlE2ETests.cs`** (new) — `[AspireStackE2EFact]` test per Decisions #2–#4: the same **8088 port-in-use fail-fast guard** as the other authenticated e2e tests, boot the stack, log in via `/login?returnUrl=/accounts` as `teller1`, assert the landing `Uri.AbsolutePath == "/accounts"`, and `GET /accounts` renders a **specific seeded account row**.
- **Validation** — `dotnet build` 0/0; the full e2e suite **6/6** under `RUN_ASPIRE_E2E=1` (the existing 5 + the new one, incl. the refactored ApprovalsAntiforgery still green); `harness lint` green; LF / no-BOM.

## User-approval gates

None.

## Exit criteria

- `SignInReturnUrlE2ETests` proves `/login?returnUrl=/accounts` lands the signed-in user on `/accounts` (not `/`) against the real stack.
- `ApprovalsAntiforgeryE2ETests` still passes after the extraction (behavior-preserving refactor).
- Full e2e suite **6/6** under `RUN_ASPIRE_E2E=1`; `dotnet build` 0/0; `harness lint` green; LF / no-BOM. No non-test source changes.

## Risks + open questions

- **E2E flakiness** — mitigated by reusing the already-passing OIDC-login flow verbatim (only parametrized/return-typed), the pinned 8088, and sequential boots.
- **Refactor could regress `ApprovalsAntiforgeryE2ETests`** — mitigated by keeping the moved logic identical and re-running the full e2e suite (both tests) as the acceptance gate.
- **Landing-URL detection** — the driver ends when it reaches a non-redirect, non-login page; after the `/signin-oidc` callback the OIDC handler 302s to `RedirectUri` (the sanitized `returnUrl`), so the final `Uri` is `/accounts`. If a future handler change alters that, the assertion fails loudly (correct).
- **Scope.** This e2e validates the **explicit** `/login?returnUrl=…` OIDC round-trip (the mechanism CS65 added). The token-expiry-notice → rendered sign-in-link click path is already covered by CS65's in-process tests (`SessionExpiredNoticeTests` asserts the notice link carries the encoded `returnUrl`); this test does not re-simulate a token expiry.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | gpt-5.5 | claude-opus-4.8 | rubber-duck (cs68-plan-review) | 654622712be5 | 2026-07-09T04:35:00Z | Go-with-amendments | Facts verified (private LoginViaOidcAsync, E2EStack, no-parallel, /accounts [Authorize]). Amendments: LoginAsync returns final app page; assert seeded CONTOSO-CHK-0001; 8088 guard; scope note. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| (populated at claim time per § Claim) | planned | — | — |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Plan-vs-implementation review

> _(filled at close-out per the gate)_
