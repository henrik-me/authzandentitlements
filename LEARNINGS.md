# Learnings

Learnings filed during the project. See [`RETROSPECTIVES.md`](RETROSPECTIVES.md) for harvest procedure and entry format.

---

> **ID sequencing:** Use sequential IDs starting from LRN-001. The linter emits
> warnings for gaps in the sequence but treats them as non-fatal; gaps do not
> cause exit code 1.

---

## Open

### LRN-001

```yaml
id: LRN-001
date: 2025-01-01
category: process
source_cs: CS01
status: open
tags: [example]
```

**Problem:** _Describe the concrete problem or failure observed._

**Finding:** _State the durable insight in one or two sentences._

**Disposition:** _(leave blank until dispositioned; required for `applied`/`obsolete` status)_

---

## Applied

### LRN-127

```yaml
id: LRN-127
date: 2026-05-14
category: process
source_cs: CS48
status: applied
tags: [review-evidence, independence-invariant, sub-agents, self-review, rubber-duck]
claim_area: review-gates
```

**Problem:** The sub-agent dispatch/reporting path allowed implementers to
report their own diff review without stating that the result is not review
evidence. Orchestrators could mistakenly treat implementer self-review as the
rubber-duck review required by REVIEWS.md Phase 2.

**Finding:** **Implementer self-review is not a rubber-duck review.** A
self-review by the implementing agent does not satisfy `REVIEWS.md § Phase 2`.
Always dispatch a separate reviewer sub-agent (or use the
`harness review <pr>` CLI) whose model is independent from every implementer
model used in the CS.

**Evidence:** `henrik-me/sub-invaders` PR #28 (CS07 content) had an
implementer self-report of "no findings". A later independent GPT-5.5 review
raised a No-Go wave-skip finding around `?startWave=N`; the canonical PR #28
fixture records that finding as disputed/withdrawn after live verification, but
the durable failure remained: implementer self-review had been treated as review
evidence instead of requiring independent, SHA-pinned review-of-record output.

**Disposition:** Applied in CS48 / issue #142. The sub-agent dispatch/reporting
template now states that self-review carries zero review weight, asks for
`Implementer model used` instead of implementer review evidence, extends the
clickstop implementer-not-reviewer lint rule to model overlap, and points
orchestrators at the `harness review <pr>` CLI for the independent
rubber-duck review. Regression coverage lives in
`tests/cs48-implementer-self-review-ban.test.mjs`.


## Obsolete

_(no entries yet)_

## Deferred

_(no entries yet)_
