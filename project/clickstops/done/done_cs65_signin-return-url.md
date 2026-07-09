# CS65 — Preserve the originating page through sign-in (OIDC return-URL)

**Status:** done
**Owner:** omni-ae-c2
**Branch:** cs65/content
**Started:** 2026-07-08
**Closed:** 2026-07-08
**Filed by:** omni-ae-c2 on 2026-07-08 — maintainer report: after an access-token expiry on a page (e.g. an account page), clicking "Sign in again" re-authenticates but drops the user on the home page instead of returning to the page they were on. The originating page/state is not preserved across the OIDC round-trip.
**Depends on:** none (builds on the CS03 OIDC login + CS59/#219 expired-token UX; independent of other in-flight CSs)

## Goal

Return the user to the page they were on when they clicked a sign-in link — after a token-expiry notice, an "authorization required" prompt, or the nav/home "Log in" — instead of always landing on `/`. Do it by threading a **validated, local** `returnUrl` through the OIDC challenge, with open-redirect protection.

## Background

- **The flow today.** An expired/invalid access token makes a downstream API call return `401 invalid_token`; `AuthChallengeState.Capture` records it and `Components/SessionExpiredNotice.razor` renders a "Your session has expired … Sign in again" notice whose link is `href="login"`. `Program.cs`'s `/login` endpoint issues the OIDC challenge with a **hardcoded** redirect:
  ```csharp
  app.MapGet("/login", () =>
      Results.Challenge(new AuthenticationProperties { RedirectUri = "/" }, [OidcScheme]));
  ```
  After the Keycloak round-trip (`/signin-oidc` callback → `RedirectUri`), the user always lands on `/` — the originating page is never captured, so it cannot be restored.
- **All four sign-in entry points share the gap** (none pass a return URL): `Components/SessionExpiredNotice.razor` ("Sign in again"), `Components/Routes.razor` `NotAuthorized` ("Log in"), `Components/Layout/NavMenu.razor` ("Log in"), `Components/Pages/Home.razor` ("Log in"). `Clients/AccessTokenHandler.cs` only *attaches* the bearer token (fail-closed) and never issues a challenge, so the post-login redirect target is set exclusively by these links.
- **Standard fix.** The ASP.NET Core "return URL" pattern: the sign-in links carry the current relative URL as a `returnUrl` query parameter, and `/login` uses it as the challenge `RedirectUri` — but only after validating it is a **local** URL, because `returnUrl` is attacker-influenceable (a phishing `…/login?returnUrl=//evil.com`) and flows into a post-authentication redirect.
- **Renders in static SSR.** These components render as static server-side HTML during the request (that is how `AccessTokenHandler` reads the per-request `HttpContext` token and `AuthChallengeState` captures the 401), so `NavigationManager` is available and `NavigationManager.Uri` is the current request URL — the value to preserve.
- **Prior art.** CS59 (#206) and the bank-web auth UX work (#219) already own this `/login`+`/logout`+notice surface; this CS extends `/login` and the four links only.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | How the originating page is captured | Each sign-in link passes the current page as a `returnUrl` query parameter to `/login`, computed from `NavigationManager` (`"/" + Nav.ToBaseRelativePath(Nav.Uri)` — the relative path **and** query) and encoded with `Uri.EscapeDataString` | `NavigationManager` resolves the current URL in static SSR where the notice renders; carrying the path+query restores page state that lives in the URL (account id, selected tab). `EscapeDataString` keeps `?`/`&`/`#` in the origin URL from corrupting the query param. |
| 2 | `returnUrl` validation (open-redirect + loop guard) | `/login` binds `string? returnUrl` and uses it as the OIDC `RedirectUri` **only if** it passes an `IsLocalUrl`-equivalent check — non-empty, starts with a single `/` (not `//` and not `/\`) — **and** its path is not itself an auth endpoint (`/login`, `/logout`); otherwise fall back to `/` | The value is attacker-influenceable and becomes a post-login redirect target. A local-only check (mirroring `IUrlHelper.IsLocalUrl` / `LocalRedirect`) rejects `//evil.com`, `/\evil`, absolute (`https://…`) and scheme (`javascript:`) URLs; excluding the auth endpoints prevents a post-OIDC redirect loop back through sign-in. **Fail-safe default** is `/`. |
| 3 | Entry-point scope | Thread `returnUrl` from **all four** links (SessionExpiredNotice, Routes `NotAuthorized`, NavMenu "Log in", Home "Log in") via a single shared render fragment/component so the encoding + validation contract lives in one place | Maintainer-selected scope; any sign-in returns the user where they were. A shared `SignInLink` component avoids four copies of the URL-building logic drifting apart. |
| 4 | What is (and is not) preserved | Preserve the **relative path + query** only; explicitly **not** transient in-memory form input | URL state round-trips cleanly through the redirect; unsubmitted in-memory form state cannot survive a full external OIDC round-trip without separate pre-challenge persistence — out of scope here and recorded as a follow-up if wanted. |
| 5 | `/login` shape | Keep `/login` a `MapGet` minimal-API endpoint; add the `returnUrl` parameter + a small local-url helper; `/logout` and the OIDC options are unchanged | Minimal, reviewable surface; no change to the security-sensitive callback/HTTPS-metadata configuration or to `/logout` (CS59). |

## Deliverables

- **`src/AuthzEntitlements.Bank.Web/Program.cs`** — `/login` binds `string? returnUrl` and issues `Results.Challenge` with `RedirectUri` = the sanitized `returnUrl` (or `/`), delegating validation to the helper below.
- **`src/AuthzEntitlements.Bank.Web/Clients/LoginReturnUrl.cs` (new)** — a testable `internal static` helper `SafeLocalReturnUrl(string?) -> string` that returns the value when it is a local URL (single-`/`, not `//`/`/\`) whose path is not `/login` or `/logout`, else `/`. Unit-testable directly (not a `Program.cs` local function).
- **`src/AuthzEntitlements.Bank.Web/Components/SignInLink.razor` (new)** — a shared component that injects `NavigationManager`, computes the encoded current-page `returnUrl`, and renders `<a href="login?returnUrl=…">@ChildContent</a>` with a caller-supplied CSS class (`button` / `nav-link`).
- **`src/AuthzEntitlements.Bank.Web/Components/SessionExpiredNotice.razor`** — replace the `href="login"` anchor with `<SignInLink>` ("Sign in again").
- **`src/AuthzEntitlements.Bank.Web/Components/Routes.razor`** — the `NotAuthorized` "Log in" link uses `<SignInLink>` (returns to the protected page after sign-in).
- **`src/AuthzEntitlements.Bank.Web/Components/Layout/NavMenu.razor`** — the "Log in" link uses `<SignInLink>` (`nav-link` class).
- **`src/AuthzEntitlements.Bank.Web/Components/Pages/Home.razor`** — the "Log in" link uses `<SignInLink>`.
- **`tests/AuthzEntitlements.Bank.Web.Tests/`** — (a) extend `SessionExpiredNoticeTests` to assert the rendered "Sign in again" link includes `login?returnUrl=` with the encoded current page (e.g. `%2Faccounts%2F…`), and add lightweight coverage that the other three entry points (Routes `NotAuthorized`, NavMenu, Home) also render `login?returnUrl=`; (b) a focused unit test of the `SafeLocalReturnUrl` helper covering the vectors — honored (`/`, `/accounts/x`, `/accounts/x?tab=tx`) and rejected→`/` (`//evil.com`, `/\evil`, `https://evil.com`, `javascript:…`, `/login`, `/logout`, `""`/null); (c) a `/login?returnUrl=…` endpoint test asserting a local `returnUrl` is honored as the challenge `RedirectUri`, and that an external one **and** `/login` both fall back to `/`. Minimum: cover honored-local, rejected-external, auth-endpoint-loop, and null/empty.
- **Validation** — `dotnet build` 0/0; `dotnet test` green (new tests included); `harness lint` green; LF / no-BOM.

## User-approval gates

None — the maintainer selected the all-four-entry-points scope at filing time. The in-memory-form-state limitation (Decision #4) is surfaced, not silently chosen.

## Exit criteria

- `/login?returnUrl=<local>` post-login redirects to that local page; `/login?returnUrl=<external-or-protocol-relative>` falls back to `/` (no open redirect).
- All four sign-in links render `login?returnUrl=<encoded current page>`.
- After a token expiry on the account page, "Sign in again" returns the user to that account page (path + query) with a fresh token, showing real content (not the expiry notice).
- `dotnet build` 0/0; `dotnet test` green; `harness lint` green; LF / no-BOM; `AppHost.cs`, `/logout`, and the OIDC options are unchanged.

## Risks + open questions

- **Open redirect** if the local-url check is wrong — mitigated by the `IsLocalUrl` guard + explicit tests for `//`, `/\`, absolute and scheme URLs; fail-safe default `/`.
- **`NavigationManager` in the `Routes.razor` `NotAuthorized` template / static SSR** — verify it resolves the current (protected) page URL in that render path; if a context makes it unavailable, that link degrades to a plain `href="login"` (returns home) rather than breaking.
- **Encoding edge cases** — origin URLs containing `?`, `&`, or `#`: handled by `Uri.EscapeDataString` on write and minimal-API model-binding decode on read; covered by a test.
- **No re-auth loop** — after sign-in a fresh access token is issued (SaveTokens), so the returned page's API call succeeds; the expiry notice does not re-fire.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | gpt-5.5 | claude-opus-4.8 | rubber-duck (cs65-plan-review) | d6a5c07ca847 | 2026-07-08T22:56:00Z | Go-with-amendments | Facts verified (hardcoded RedirectUri; 4 links; SSR NavigationManager). Applied 3 amendments: reject /login,/logout (loop guard); cover all 4 links; testable SafeLocalReturnUrl helper. |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| Implement /login returnUrl + `SafeLocalReturnUrl` helper (open-redirect + loop guard) | pending | omni-ae-c2 | agent-id=omni-ae-c2 \| role=impl \| report-status=pending \| learnings=0 |
| Shared `SignInLink` component + wire all 4 sign-in links | pending | omni-ae-c2 | agent-id=omni-ae-c2 \| role=impl \| report-status=pending \| learnings=0 |
| Tests: `SafeLocalReturnUrl` unit + 4-entry-point returnUrl link coverage | pending | omni-ae-c2 | agent-id=omni-ae-c2 \| role=test \| report-status=pending \| learnings=0 |
| Close-out: docs + restart state | pending | omni-ae-c2 | Update WORKBOARD + CONTEXT.md after merge so a fresh agent restarts from actual state |
| Close-out: learnings + follow-ups | pending | omni-ae-c2 | File/disposition learnings in LEARNINGS.md; open follow-up CSs for any unresolved issues |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | omni-ae-c2 |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-09T00:35:00Z
**Outcome:** GO

Independent GPT-5.5 rubber-duck reviewed the CS65 plan (Decisions + Deliverables + Exit criteria) against the merged implementation (content PR #227 `42255bd` + endpoint-test follow-up #228 `9d6802d`) and the validation (build 0/0; full-solution test all green — Bank.Web.Tests 237/237; harness lint 23/0). A prior PVI round returned NEEDS-FIX for the missing `/login` endpoint test (deliverable c); that gap was closed by #228 and this re-review is GO.

### Per-deliverable outcome

| Deliverable | Outcome | Notes |
|---|---|---|
| `Program.cs` `/login` binds `returnUrl`, challenges with sanitized `RedirectUri` | match | Uses `LoginReturnUrl.SafeLocalReturnUrl(returnUrl)` for `AuthenticationProperties.RedirectUri`. |
| `Clients/LoginReturnUrl.cs` helper | match | Fail-safe `/`; local-only single-slash guard; rejects `//`, `/\`, absolute/scheme URLs, control chars, and `/login`/`/logout` loops. |
| `Components/SignInLink.razor` shared component | match | `NavigationManager` path+query via `ToBaseRelativePath`, `Uri.EscapeDataString`-encoded; caller CSS/content. |
| `SessionExpiredNotice` / `Routes` NotAuthorized / `NavMenu` / `Home` use `<SignInLink>` | match | All four sign-in links use the shared component; Routes NotAuthorized is a defensive fallback (anonymous → challenged to Keycloak), transitively covered. |
| `Bank.Web.csproj` `InternalsVisibleTo` | added | Enables direct unit-testing of the internal helper; low-risk. |
| Tests (helper vectors, link rendering, `/login` endpoint) | match | Deliverable (c) met by `LoginEndpointReturnUrlTests.cs` (decorates `IAuthenticationService`, asserts the captured `ChallengeAsync` `RedirectUri` == sanitized `returnUrl`). |
| Validation — build/test/lint/LF no-BOM | match | build 0/0; Bank.Web.Tests 237/237; harness lint 23/0. |

### Test-coverage assessment

**sufficient** — local return URLs (incl. query), open-redirect vectors (`//`, `/\`, absolute, control-char), `/login`/`/logout` loop guards, empty/null fallback, all four link sites (direct + shared-component), and the `/login` endpoint challenge-`RedirectUri` regression test.

No blocking plan-vs-implementation gaps found.
