# CS08 — Unit tests for the ABAC conditions showcase (conditions.rego).
#
# Exercises each condition's allow and deny sides plus the aggregate `allow`.
# This is showcase coverage only — see the boundary note in conditions.rego.
#
# Run: opa test infra/opa/policy -v

package authz.bank.conditions_test

import data.authz.bank.conditions

# A baseline request that satisfies every condition (aggregate allow == true).
good_input := {
	"amount": 12000,
	"time": {"hour": 14, "weekday": "Tue"},
	"geo": {"country": "US"},
	"risk": {"score": 20},
	"customer": {"tier": "gold"},
}

# ---------------------------------------------------------------------------
# Per-condition allow/deny sides.
# ---------------------------------------------------------------------------

test_within_tier_limit_true if {
	conditions.within_tier_limit with input as good_input
}

test_within_tier_limit_false_over_ceiling if {
	over := object.union(good_input, {"amount": 150000})
	not conditions.within_tier_limit with input as over
}

test_within_business_hours_true if {
	conditions.within_business_hours with input as good_input
}

test_within_business_hours_false_after_hours if {
	evening := object.union(good_input, {"time": {"hour": 20, "weekday": "Tue"}})
	not conditions.within_business_hours with input as evening
}

test_within_business_hours_false_on_weekend if {
	weekend := object.union(good_input, {"time": {"hour": 14, "weekday": "Sat"}})
	not conditions.within_business_hours with input as weekend
}

test_from_allowed_geo_true if {
	conditions.from_allowed_geo with input as good_input
}

test_from_allowed_geo_false if {
	blocked := object.union(good_input, {"geo": {"country": "XX"}})
	not conditions.from_allowed_geo with input as blocked
}

test_low_risk_true if {
	conditions.low_risk with input as good_input
}

test_low_risk_false if {
	risky := object.union(good_input, {"risk": {"score": 80}})
	not conditions.low_risk with input as risky
}

test_requires_step_up_on_high_risk if {
	risky := object.union(good_input, {"risk": {"score": 75}})
	conditions.requires_step_up with input as risky
}

test_requires_step_up_on_high_value if {
	big := object.union(good_input, {"amount": 60000})
	conditions.requires_step_up with input as big
}

test_no_step_up_on_baseline if {
	not conditions.requires_step_up with input as good_input
}

# ---------------------------------------------------------------------------
# Aggregate decision.
# ---------------------------------------------------------------------------

test_allow_true_when_all_conditions_met if {
	conditions.allow with input as good_input
}

test_allow_false_when_after_hours if {
	evening := object.union(good_input, {"time": {"hour": 22, "weekday": "Tue"}})
	not conditions.allow with input as evening
}

test_allow_false_when_high_value_requires_step_up if {
	big := object.union(good_input, {"amount": 75000})
	not conditions.allow with input as big
}
