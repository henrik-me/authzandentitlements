# CS59 — Fix bank-web sign-out ("Missing parameters: id_token_hint")

**Status:** done
**Owner:** yoga-ae-c4
**Branch:** cs59/content
**Started:** 2026-07-06
**Closed:** 2026-07-06
**Filed by:** yoga-ae-c4 on 2026-07-06 — user reported that signing out of `bank-web` shows a Keycloak error page: **"We are sorry… Missing parameters: id_token_hint"**. Root-caused + fix verified live before filing.
**Depends on:** none (fixes a bank-web RP-initiated-logout defect; independent of the CS56/CS57/CS58 `aspire run` work)

## Goal

Make `bank-web` sign-out robust when there is **no active OIDC session**. Today `/logout` always drives an OIDC RP-initiated logout; when the local session is already gone (expired cookie, a prior sign-out, or an otherwise-unavailable `id_token`), the handler still redirects to Keycloak's end-session endpoint with a `post_logout_redirect_uri` but **no `id_token_hint`** — and Keycloak requires that parameter whenever a redirect URI is supplied, so the logout dead-ends on Keycloak's **"We are sorry… Missing parameters: id_token_hint"** page. Guard that path so an inactive-session logout clears the local cookie and returns home; the authenticated path (which already carries `id_token_hint`) is unchanged.

## Background

Reproduced live against the running stack (`aspire run`, Keycloak on `8088`), driving the **real browser OIDC code flow**:

- **How the handler builds the hint.** ASP.NET Core's `OpenIdConnectHandler.SignOutAsync` sets `message.IdTokenHint = await Context.GetTokenAsync(Options.SignOutScheme, "id_token")` (verified in the aspnetcore source) — it sources the hint from the **saved cookie tokens** (`SaveTokens = true`, `SignOutScheme` = the cookie sign-in scheme), **not** from the `AuthenticationProperties` passed to `SignOut`.
- **Authenticated ⇒ works.** With a real signed-in `teller1` session, `GET /logout` → 302 to Keycloak's end-session **with `id_token_hint` present** → logout completes. So the endpoint is correct for the normal path.
- **No active session ⇒ the bug.** `GET /logout` **without a valid session cookie** (session expired, already signed out, or the large chunked auth cookie was lost) → `GetTokenAsync("id_token")` returns null → the handler still emits `post_logout_redirect_uri` (the `SignedOutCallbackPath` `/signout-callback-oidc`) with **no `id_token_hint`**.
- **Why Keycloak 400s.** The dev realm's `bank-web` client has `redirectUris: ["*"]` and `post.logout.redirect.uris: "+"`, so Keycloak has a post-logout-redirect allow-list and (per the OIDC RP-Initiated Logout spec) requires `id_token_hint` to validate the supplied `post_logout_redirect_uri`. Absent it, it returns **HTTP 400 "We are sorry…"** — the exact reported error.
- **Reproduced (this session).** Unauthenticated `GET /logout` → 302 to `…/logout?post_logout_redirect_uri=…` with **no** `id_token_hint`; following it → Keycloak **400 "We are sorry…"**. The authenticated browser flow → `id_token_hint` present → clean logout.
- **When users hit it.** Any time `/logout` runs without a retrievable `id_token`: an expired cookie/SSO session while the page still shows "Log out", a stale or repeated logout, or the browser dropping the large chunked `.AspNetCore.Cookies` (SaveTokens + userinfo claims make it multi-chunk).

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Fix mechanism | Guard `/logout`: `var idToken = await httpContext.GetTokenAsync("id_token"); if (string.IsNullOrEmpty(idToken)) { await httpContext.SignOutAsync(Cookie); return Results.LocalRedirect("/"); } return Results.SignOut([Cookie, Oidc]);` — when there is **no** `id_token`, clear any local cookie and go home instead of bouncing through the IdP's end-session (which would 400); when authenticated, the normal RP-initiated logout carries `id_token_hint` correctly | Directly fixes the reproduced error at its source (no `id_token_hint` ⇒ don't send `post_logout_redirect_uri` to Keycloak); minimal + localized to the one sign-out path; touches no OIDC/security options; graceful — an inactive session is signed out locally rather than shown an error page |
| 2 | Rejected alternatives | Do **not** (a) stash the `id_token` in the sign-out `AuthenticationProperties`, (b) always drive the OIDC logout, (c) drop `SignedOutCallbackPath`/`post_logout_redirect_uri`, or (d) inject the hint via `OnRedirectToIdentityProviderForSignOut` | (a) **does not work** — the handler reads the hint from `GetTokenAsync(SignOutScheme,"id_token")`, not the passed properties (verified in the aspnetcore source); (b) *is* the bug; (c) loses the return-to-app UX and still leaves the inactive-session path awkward; (d) the hint is genuinely unavailable when the session is gone, so an event cannot supply it |
| 3 | Validation | Reproduce the exact error (unauthenticated `/logout` → Keycloak **400 "We are sorry…"**) and confirm the authenticated browser flow emits `id_token_hint`; after the fix, unauthenticated `/logout` returns **home** (no IdP bounce) and the authenticated logout is unchanged. Build 0/0 + default test suite green + `harness lint` 0. A **permanent** browser-code-flow logout e2e is a noted follow-up (authorization-code automation through the Keycloak login form + cross-port cookies is fragile to keep in CI), consistent with the CS57/CS58 deferral of the authenticated-UI drive-through | The live reproduction pins the true root cause (inactive-session logout, not a properties issue) and the guard is a small, verifiable change |

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | gpt-5.5 | claude-opus-4.8 | rubber-duck | cdc4bb5b09c8 | 2026-07-06T15:33:01Z | Go | Corrected saved-cookie id_token diagnosis; guard prevents the no-hint Keycloak bounce while preserving normal logout. No blockers. |

## Deliverables

- **D1 — `src/AuthzEntitlements.Bank.Web/Program.cs`.** The `/logout` endpoint guards the inactive-session path: if `GetTokenAsync("id_token")` is empty, sign out the local cookie and `Results.LocalRedirect("/")`; otherwise the normal `Results.SignOut([Cookie, Oidc])`. Comment cites the root cause.
- **D2 — `LEARNINGS.md`.** New LRN: ASP.NET Core `OpenIdConnectHandler.SignOutAsync` sources `id_token_hint` from `Context.GetTokenAsync(SignOutScheme, "id_token")` (the saved cookie tokens), **not** the sign-out `AuthenticationProperties`. When `/logout` runs with no active session the hint is empty, yet the handler still sends `post_logout_redirect_uri` → Keycloak 400 "Missing parameters: id_token_hint". Guard: with no id_token, sign out the cookie + redirect home instead of the OIDC end-session bounce.
- **D3 — docs (if applicable).** If `docs/product/bank-web.md` (or an auth doc) documents the login/logout flow, note the inactive-session logout behaviour; otherwise skip (no stale doc to correct).

## User-approval gates

- **Bug reported by the user** ("Missing parameters: id_token_hint" on sign-out). No further gate to implement.
- **Exit gate:** the reproduction + fix confirmation recorded in the CS file before close-out.

## Exit criteria

- `dotnet build AuthzEntitlements.sln` 0 warnings / 0 errors; default `dotnet test AuthzEntitlements.sln` green (no new failures); `harness lint` 0 failed; LF/no-BOM.
- **Reproduction + fix evidence** recorded in the CS file: unauthenticated `/logout` → Keycloak **400 "We are sorry…"** before the fix; → **local redirect home** after; the authenticated browser flow emits `id_token_hint` and is unchanged.
- The `/logout` endpoint **no longer drives the OIDC end-session when there is no `id_token`** (verified by the diff + the live reproduction).

## Risks + open questions

- **R1 — graceful degradation.** If a session is authenticated but the `id_token` is unavailable (e.g., a dropped cookie chunk), the guard routes to cookie-only sign-out + home rather than an error page — the user is logged out locally; any IdP session expires or can be ended at Keycloak. Acceptable and strictly better than the 400.
- **R2 — no automated logout e2e (deferred).** The browser-flow logout is not covered by the CS57/CS58 e2e (password-grant based). The live browser-flow reproduction + the reviewed diff are the validation; a permanent authorization-code-flow logout e2e is a noted follow-up.
- **R3 — handler contract (confirmed).** `OpenIdConnectHandler.SignOutAsync` sources `id_token_hint` from `Context.GetTokenAsync(SignOutScheme, "id_token")` (verified in the aspnetcore source), so the fix **guards the endpoint** rather than manipulating sign-out properties; there is no dependency on the (incorrect) properties path.

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| D1 — `Program.cs` `/logout` guard (no id_token → cookie sign-out + LocalRedirect("/")) | done | yoga-ae-c4 / impl sub-agent | merged in #206 |
| D2 — `LEARNINGS.md` LRN (handler sources id_token_hint from GetTokenAsync; guard the inactive-session path) | done | yoga-ae-c4 | LRN-090 on main |
| D3 — docs (bank-web/auth doc) if a logout flow is documented | done | impl sub-agent | skipped — no logout section |
| Validate — reproduce (unauth → 400) + confirm fix (unauth → home; auth → id_token_hint) live | done | yoga-ae-c4 | reproduced + confirmed via real browser OIDC flow |
| Review — GPT-5.5 content review + Copilot (A5 ordering) | done | yoga-ae-c4 | R1→R2 Go; Copilot nit applied + CSRF deferred (threads resolved) |
| Close-out — PVI gate, active→done, WORKBOARD/CONTEXT, LRN applied | done | yoga-ae-c4 | this PR |

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c4 |
| Reviewer agent | rubber-duck |

## Notes / Learnings

- **2026-07-06 — Closed.** `/logout` guard merged in PR #206 (squash `6ae8c8e`). Root cause + fix reproduced/validated live (unauthenticated `/logout` → Keycloak 400 "We are sorry…" before; → home after; authenticated `/logout` → `id_token_hint` present). Filed **LRN-090** (`OpenIdConnectHandler` sources `id_token_hint` from `GetTokenAsync(SignOutScheme,"id_token")`, not the sign-out properties; guard the inactive-session path). Follow-up noted: harden the GET `/logout` against logout-CSRF (POST + antiforgery) in a dedicated change.

## Plan-vs-implementation review

**Reviewer:** yoga-ae-c4 (orchestrator) — independent content review of record by gpt-5.5 (rubber-duck), R1→R2
**Date:** 2026-07-06
**Outcome:** GO

Merged content: PR #206 (`6ae8c8e`). Independent content review: gpt-5.5 rubber-duck R1 **Go** (no bugs, plan-conformant, security OK) → R2 **Go** (Copilot nit: explicit cookie-scheme token lookup). Root cause + fix reproduced and validated live via the real browser OIDC code flow (unauthenticated `/logout` → Keycloak 400 "We are sorry…" before; → 302 home after; authenticated `/logout` → `id_token_hint` present, clean logout).

| Plan item | Implemented? | Evidence / notes |
|---|---|---|
| Decision #1 — guard `/logout`: no id_token → cookie sign-out + `LocalRedirect("/")`; else normal `SignOut([Cookie, Oidc])` | ✅ | `Program.cs` (`GetTokenAsync(CookieAuthenticationDefaults.AuthenticationScheme,"id_token")` guard) |
| Decision #2 — rejected the properties-stash / always-OIDC / drop-redirect / event alternatives | ✅ | Handler contract confirmed (`GetTokenAsync(SignOutScheme,"id_token")`); guard chosen |
| Decision #3 — validation: reproduce + confirm; defer a permanent browser-flow e2e | ✅ | Live reproduction + fix confirmation recorded; e2e deferred (fragile code-flow + cross-port cookies) |
| D1 — `Program.cs` guard | ✅ | merged |
| D2 — `LEARNINGS.md` LRN-090 | ✅ | on main |
| D3 — docs | ✅ (skipped) | `docs/product/bank-web.md` documents no logout flow — correctly untouched |
| Exit — build 0/0; default tests green; harness lint 0; reproduction+fix evidence | ✅ | all verified this session |

**Notes:** (1) **Logout-CSRF follow-up (out of scope):** the GET `/logout` is pre-existing and theoretically susceptible to logout-CSRF (forced sign-out, low severity); hardening to POST-with-antiforgery touches the NavMenu/Home links and warrants its own reviewed change — noted for a follow-up (Copilot thread resolved with this rationale). (2) **Content PR #206 was admin-merged** only to clear a spurious GitHub block: two stale `read-only-gates` check runs (from before the R2 Go row was added) lingered on the head while the *latest* run and every other required check were SUCCESS; re-runs only accumulate check runs. No real gate was bypassed.
