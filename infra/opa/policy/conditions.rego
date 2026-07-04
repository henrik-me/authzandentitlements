# CS08 — ABAC capability showcase (policy-layer demonstration only).
#
# This module demonstrates OPA/Rego's declarative reach over rich attributes —
# amount, time, geo, risk, and customer tier — as a taste of the attribute-based
# access-control (ABAC) policies OPA makes natural to express.
#
# BOUNDARY (read this): this is a demonstration of OPA's ABAC reach BEYOND the
# current CS05 PDP decision contract, which carries only `amount` on the resource
# (see ../../docs/authz/pdp-contract.md and
# ../../src/AuthzEntitlements.Authz.Pdp/Contracts/Resource.cs). These rules are
# NOT queried by the C# OpaDecisionProvider and NOT part of the fixed
# authz/bank/decision contract. Wiring these attributes end-to-end (subject
# location, request time, risk scoring, customer tier) through the PDP request
# shape is deliberately deferred to a FUTURE clickstop. The illustrative input
# shape below is local to this showcase and intentionally richer than the real
# AccessRequest.
#
# Illustrative input (showcase-only):
#   {
#     "amount":   12000,
#     "time":     { "hour": 14, "weekday": "Tue" },
#     "geo":      { "country": "US" },
#     "risk":     { "score": 20 },
#     "customer": { "tier": "gold" }
#   }

package authz.bank.conditions

import rego.v1

# Countries from which requests are accepted in this illustration.
allowed_countries := {"US", "CA", "GB", "DE"}

# Per-tier single-transaction ceilings — higher tiers may move more per request.
tier_limits := {"standard": 10000, "silver": 25000, "gold": 100000, "platinum": 500000}

# amount: the request is within the customer tier's single-transaction ceiling.
within_tier_limit if {
	limit := tier_limits[input.customer.tier]
	input.amount <= limit
}

# time: the request falls inside standard business hours on a weekday (09:00–16:59).
within_business_hours if {
	input.time.hour >= 9
	input.time.hour < 17
	not is_weekend
}

is_weekend if input.time.weekday in {"Sat", "Sun"}

# geo: the request originates from an allowed country.
from_allowed_geo if input.geo.country in allowed_countries

# risk: the request's risk score is below the step-up threshold.
low_risk if input.risk.score < 50

# risk + amount: a high-risk OR high-value request must be stepped up (MFA /
# second approver) before it can be permitted straight through.
requires_step_up if input.risk.score >= 50

requires_step_up if input.amount >= 50000

# Aggregate showcase decision: permit only when every condition is satisfied and
# no step-up is required — a declarative ABAC "all of" over the rich attributes.
default allow := false

allow if {
	within_tier_limit
	within_business_hours
	from_allowed_geo
	low_risk
	not requires_step_up
}
