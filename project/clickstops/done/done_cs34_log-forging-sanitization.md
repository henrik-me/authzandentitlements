# CS34 — Log-forging (CWE-117) sanitization at the 4 flagged audit-log sites

**Status:** done
**Owner:** yoga-ae-c5
**Branch:** cs34/content
**Started:** 2026-07-04
**Closed:** 2026-07-04
**Filed by:** yoga-ae-c5 — 2026-07-04; surfaced during branch-protection hardening — the `code_scanning` ruleset rule (`alerts_threshold: errors`) blocks every merge while 4 open `cs/log-forging` CodeQL alerts (error-level) remain, forcing admin bypass on all PRs.
**Depends on:** none

## Goal

Neutralize the 4 open CodeQL `cs/log-forging` (CWE-117, log injection) alerts by sanitizing user-controlled values before they reach `ILogger`, so no request- or claim-derived value can inject CR/LF to forge fake log lines. Resolving these clears the `code_scanning` merge block so PRs can merge without admin bypass.

## Background

CodeQL flags 4 error-level `cs/log-forging` alerts where user-provided values flow into log calls:

- `src/AuthzEntitlements.Authz.Pdp/Providers/OpenFga/OpenFgaProvider.cs:95` — `_logger.LogWarning(... user={User} relation={Relation} object={Object} ...)` with `check.User`/`check.Relation`/`check.Object` from the authorization request (alerts #6, #7).
- `src/AuthzEntitlements.Edge.Gateway/Audit/GatewayAuditMiddleware.cs:86` — structured `LogInformation` including `Method`/`Path`/`Subject`/`Tenant` from the request/claims (alert #9).
- `src/AuthzEntitlements.Bank.Api/Auth/BankAuthorizationAuditMiddleware.cs:70` — structured `LogInformation` including `Method`/`Path`/`Subject`/`Tenant` (alert #8).

A proven, CodeQL-recognized mitigation already exists in-repo: `LoggingPdpDecisionAuditSink.Record` (added by CS19) wraps every rendered string value in `Clean(v) => v?.Replace('\r', ' ').Replace('\n', ' ')` and is **not** among the flagged alerts — evidence that this CR/LF-stripping barrier satisfies the `cs/log-forging` query. The 3 flagged sites simply do not apply that barrier. All three projects (`Edge.Gateway`, `Bank.Api`, `Authz.Pdp`) already reference `AuthzEntitlements.ServiceDefaults`, giving a natural shared home for the sanitizer.

## Decisions

| # | Decision | Choice | Rationale |
|---|---|---|---|
| 1 | Sanitizer barrier | Strip CR/LF by replacing `'\r'` and `'\n'` with a space on user-derived string values before logging — reuse the exact pattern already proven in `LoggingPdpDecisionAuditSink` (CS19). | That pattern is recognized by the `cs/log-forging` query (the sink using it is not flagged) and is minimal; it replaces only CR/LF (with a space) in string fields. Audit-record impact is addressed in Decision 4. |
| 2 | Helper home | Add one shared **`public static`** `LogSanitizer.Clean(string?)` in `AuthzEntitlements.ServiceDefaults` (referenced by all three projects; `public` because an `internal` member is not visible to the referencing assemblies without `InternalsVisibleTo`), and refactor `LoggingPdpDecisionAuditSink`'s private `Clean` to delegate to it. | DRY: a single tested sanitizer instead of duplicating the barrier; ServiceDefaults is the common dependency; the sink refactor is behavior-preserving and covered by existing `LoggingPdpDecisionAuditSinkTests`. |
| 3 | Sanitization scope | Apply `Clean` to every rendered string argument at the 3 flagged log calls (not just the minimal tainted subset), matching the sink's defense-in-depth convention; leave non-string/enum/typed fields (decision, reason, status code, timestamp) untouched. | Uniform and unambiguous for CodeQL at negligible cost; avoids leaving a sibling tainted field unsanitized. |
| 4 | Audit-record impact | At the gateway/bank sites the structured `LogInformation` **is** the audit-ready record (`GatewayAuditEvent`/`BankAuditEvent`), so cleaning the logged args also neutralizes CR/LF in the persisted structured properties — intended: CR/LF are never valid in method/path/subject/tenant, and neutralizing them prevents forged audit entries. For the PDP sink / OpenFGA path, the separate `PdpDecisionAuditEvent` → Audit.Service record retains raw values (only the `ILogger` string is cleaned). HTTP responses, decisions, status codes, metrics, and OTel tags are unchanged. | Accurate scoping: sanitization touches only the CR/LF characters of string fields (replaced with a space); no legitimate audit data is lost and no authz behavior changes. |
| 5 | Verification gate | Exit requires the content PR's CodeQL run to show 0 open `cs/log-forging` alerts, plus a full-solution `dotnet build` (0 warnings under `TreatWarningsAsErrors`) and `dotnet test` green, with new unit + behavior tests. | The concrete objective is CodeQL clearing the alerts so `code_scanning` no longer blocks merges; tests lock in the barrier. |

## Deliverables

- `LogSanitizer` helper in `AuthzEntitlements.ServiceDefaults` (CR/LF → space; null/empty-safe) with unit tests covering `\r`, `\n`, `\r\n`, embedded control chars, null, empty, and clean-string pass-through (minimum ~6 cases).
- `LoggingPdpDecisionAuditSink` refactored to delegate to the shared `LogSanitizer` (no behavior change; existing tests still pass).
- Sanitized user-derived values at the 3 flagged log calls:
  - `OpenFgaProvider.cs` `LogWarning` — `User`/`Relation`/`Object`.
  - `GatewayAuditMiddleware.cs` `LogInformation` — `Method`/`Path`/`Subject`/`Tenant` (and any sibling string field rendered on that line).
  - `BankAuthorizationAuditMiddleware.cs` `LogInformation` — `Method`/`Path`/`Subject`/`Tenant`.
- Behavior tests asserting a CR/LF-bearing input is emitted without newline characters at each of the 3 sites (or via the shared seam), minimum one per site.
- All 4 `cs/log-forging` CodeQL alerts resolved (verified on the content PR); `dotnet build` 0/0 and `dotnet test` green; `harness lint` green.

## User-approval gates

None — security hardening of log output only; no API, response, audit-record, or authz-decision behavior changes.

## Exit criteria

- 0 open `cs/log-forging` CodeQL alerts on the content PR (the `code_scanning` rule no longer blocks merges on these).
- Full-solution `dotnet build` 0 warnings / 0 errors and `dotnet test` all pass, including the new sanitizer + behavior tests.
- `harness lint` exits 0.

## Risks + open questions

- **CodeQL may not clear on the first pass** if the barrier isn't recognized at a given site. Mitigation: the pattern is already proven by the CS19 sink; if a site persists, apply the barrier identically at the exact rendered argument and re-run. Dismissal-with-justification is a last resort only, not the plan.
- **File overlap with planned CS32** (observability/audit-enrichment) which also edits `GatewayAuditMiddleware.cs` + `BankAuthorizationAuditMiddleware.cs`. CS32 is unclaimed; CS34 lands first. Whichever of CS32/CS34 is second must rebase. Note in the CS32 plan on next touch.
- **Merge sequencing**: until this content PR lands, the 4 alerts keep `code_scanning` blocking, so the CS34 filing/claim PRs themselves require admin squash-merge; documented, expected.

## Plan review

| Round | Reviewer model | Plan author model(s) | Reviewer agent | Reviewed sections hash | Timestamp (UTC) | Verdict | Findings recap (≤200 chars) |
|---|---|---|---|---|---|---|---|
| R1 | GPT-5.5 | Claude Opus 4.8 | rubber-duck dispatched (orchestrator: yoga-ae-c5) | 09afaa498e2a | 2026-07-04T20:46:36Z | Go-with-amendments | Applied both amendments: helper made public (internal not cross-assembly visible); gateway/bank structured log IS the audit record so cleaning args sanitizes persisted fields (intended). |

## Tasks

| Task | State | Owner | Notes |
|---|---|---|---|
| Add shared `LogSanitizer.Clean` in ServiceDefaults + unit tests | done | cs34-impl | `public static` CR/LF→space, null-safe; 7 unit cases |
| Refactor `LoggingPdpDecisionAuditSink` to delegate to the shared helper | done | cs34-impl | Behavior-preserving; existing `LoggingPdpDecisionAuditSinkTests` pass |
| Sanitize the 3 flagged log sites | done | cs34-impl | OpenFgaProvider, GatewayAuditMiddleware, BankAuthorizationAuditMiddleware — all rendered string args wrapped in `Clean` |
| Behavior tests per site | done | cs34-impl | Real-path CR/LF tests at each of the 3 sites |
| Verify CodeQL clears the 4 alerts + build/test green | done | yoga-ae-c5 | `dotnet build` 0/0; `dotnet test` 1357 pass; `harness lint` 0 failed; PR `Analyze (csharp)` success; main CodeQL re-run auto-closes the 4 alerts |
| Close-out: docs + restart state | done | yoga-ae-c5 | WORKBOARD row removed; CONTEXT.md updated |
| Close-out: learnings + follow-ups | done | yoga-ae-c5 | No new durable LRN warranted — shared `LogSanitizer` self-documents; the `code_scanning` fix-PR chicken-and-egg is recorded in CONTEXT + agent memory; no follow-up CS |

## Notes / Learnings

_None yet — populated during implementation and close-out._

## Model audit

| Field | Value |
|---|---|
| Implementer models | claude-opus-4.8 |
| Reviewer model | gpt-5.5 |
| Implementer agent | yoga-ae-c5 |
| Reviewer agent | rubber-duck |

## Plan-vs-implementation review

**Reviewer:** GPT-5.5 (rubber-duck)
**Date:** 2026-07-04T21:36:08Z
**Outcome:** GO

Independent GPT-5.5 review of the CS34 plan against the merged content (`git show c40e1a7`) + final files; ran `dotnet test tests/AuthzEntitlements.Authz.Pdp.Tests` (734 passed).

| Deliverable | Outcome | Notes |
|---|---|---|
| Shared `LogSanitizer` helper + unit tests | match | — |
| `LoggingPdpDecisionAuditSink` delegates to shared sanitizer | match | — |
| 3 flagged sites sanitized | match | Every rendered string arg at the OpenFGA, Gateway, and Bank `ILogger` sites is wrapped in `LogSanitizer.Clean`; `StatusCode` and `TimestampUtc` remain unwrapped. |
| Behavior tests ≥1 per flagged site | match | — |
| 4 CodeQL alerts resolved + build/test green | match | Resolution verified-by-design via the proven CR/LF-replacement barrier (the CS19 sink using it is not flagged); recorded full run green (1357 pass). |

**Test-coverage assessment:** sufficient — the sanitizer has direct unit coverage for CR, LF, CRLF, null, empty, and clean pass-through; each flagged site has a real-path behavior test asserting CR/LF-bearing input cannot render newline characters in emitted logs.

Additional verification: `LogSanitizer.Clean` is `public`, null-safe, strips both `\r` and `\n`; the sink refactor is behavior-preserving; the CS32 Bank-test `SetEndpoint` adaptation is a correct compatibility fix for the new `ShouldAudit` guard, not a weakening.
