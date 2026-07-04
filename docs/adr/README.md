# Architecture Decision Records

An **Architecture Decision Record (ADR)** captures a single significant architectural
decision, the context that forced it, and the consequences that follow. Each record is a
short, immutable document written in the [Nygard format](https://cognitect.com/blog/2011/11/15/documenting-architecture-decisions):
a title, a status, the context, the decision itself, and its consequences. Once a decision
is `Accepted` the record is not rewritten — a later decision that changes course is a new
ADR that supersedes the old one, so the log stays an honest history.

The ADRs below are **retroactive**: they formalize decisions already realized and shipped
across the clickstops named in each record. Every "Decision" and "Realized in" claim is
grounded in the shipped code and the corresponding `done_csNN_*.md` clickstop, not in
aspiration. Where a decision's real scope is narrower than its title suggests, the record
says so.

## Index

| # | Title | Status | Realized in |
|---|---|---|---|
| [0001](0001-unified-authzen-aligned-pdp-abstraction.md) | Unified, AuthZEN-aligned PDP abstraction | Accepted | CS05 |
| [0002](0002-reference-engine-as-parity-oracle.md) | Reference engine as the parity oracle | Accepted | CS05 |
| [0003](0003-multi-engine-adapter-strategy-and-config-swap.md) | Multi-engine adapter strategy + config-driven swap | Accepted | CS05–CS09, CS20 |
| [0004](0004-rebac-with-openfga-for-relationships.md) | ReBAC via OpenFGA for relationships | Accepted | CS07, CS20 |
| [0005](0005-entitlements-via-openfeature-and-usage-metering.md) | Entitlements via OpenFeature + usage metering | Accepted | CS10 |
| [0006](0006-fail-closed-and-audit-first-decisioning.md) | Fail-closed + audit-first decisioning | Accepted | CS04, CS05, CS13 |

## Related evaluation material

These ADRs record *what we chose*. The evaluation lab (CS23) records *how the field
compares* and *why these choices hold up against the market*:

- [Comparison matrix](../eval/comparison-matrix.md) — the shipped engines scored across
  consistency, latency, policy language, testability, auditability, ops burden, .NET
  support, AuthZEN alignment, and licensing.
- [Market survey](../eval/market-survey.md) — a broad survey of the ReBAC, policy-engine,
  and entitlements landscape, with strengths, weaknesses, and when-to-use notes per tool.

## Authoring a new ADR

1. Copy the structure of an existing record: a `# NNNN. Title` H1, a metadata line, then the
   `## Status`, `## Context`, `## Decision`, `## Consequences`, `## Alternatives considered`,
   and `## When to use / when not` sections.
2. Number sequentially; never renumber an existing ADR.
3. Add a row to the index table above.
4. Keep it focused (~40–90 lines). Accuracy over length — do not overstate.
