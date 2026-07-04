# CS11 — Unit tests for the governance segregation-of-duties (SoD) decision.
#
# Exercises the governance.access.request action added to the authz.bank decision policy
# (authz.rego): a PROPOSED role set carried on subject.roles is denied SodConflict when it
# contains an incompatible pair, else permitted with no obligation. These assert the SAME rule
# the in-process GovernanceSodPolicy
# (../../src/AuthzEntitlements.Authz.Pdp/Providers/Sod/GovernanceSodPolicy.cs) encodes, so the
# reference and OPA engines return the same verdict.
#
# The decision is served from the SAME authz.bank package (via the total `decision` rule) that
# the C# OpaDecisionProvider already queries at the fixed /v1/data/authz/bank/decision path, so
# no adapter change is needed and the existing decision path/tests are unaffected.
#
# Run: opa test infra/opa/policy -v

package authz.bank_governance_test

import data.authz.bank

# Governance request builder: a principal proposing to hold `roles`. The governance SoD check
# ignores tenant / scope / maker, so only subject.roles is material here.
gov_request(roles) := {
	"subject": {"type": "user", "id": "user-1", "roles": roles, "tenant": "CONTOSO"},
	"action": {"name": "governance.access.request"},
	"resource": {"type": "access-grant", "id": "quarter-end-close", "tenant": "CONTOSO"},
	"context": {"scopes": []},
}

deny_sod := {"decision": "Deny", "reason": "SodConflict", "obligations": []}

permit := {"decision": "Permit", "reason": "Permit", "obligations": []}

# ---------------------------------------------------------------------------
# Conflicts: each incompatible pair denies SodConflict.
# ---------------------------------------------------------------------------

test_gov_teller_and_branchmanager_conflict if {
	d := bank.decision with input as gov_request(["Teller", "BranchManager"])
	d == deny_sod
}

test_gov_teller_and_compliance_conflict if {
	d := bank.decision with input as gov_request(["Teller", "ComplianceOfficer"])
	d == deny_sod
}

test_gov_auditor_and_teller_conflict if {
	d := bank.decision with input as gov_request(["Auditor", "Teller"])
	d == deny_sod
}

test_gov_auditor_and_branchmanager_conflict if {
	d := bank.decision with input as gov_request(["Auditor", "BranchManager"])
	d == deny_sod
}

test_gov_auditor_and_compliance_conflict if {
	d := bank.decision with input as gov_request(["Auditor", "ComplianceOfficer"])
	d == deny_sod
}

# Order independence: the pair is unordered, so the reversed set still denies.
test_gov_pair_order_independent if {
	d := bank.decision with input as gov_request(["BranchManager", "Teller"])
	d == deny_sod
}

# A conflict is detected even amid extra, non-conflicting roles.
test_gov_conflict_among_extra_roles if {
	d := bank.decision with input as gov_request(["ComplianceOfficer", "Auditor", "SomeOtherRole"])
	d == deny_sod
}

# ---------------------------------------------------------------------------
# Allowed combinations: independent role sets permit.
# ---------------------------------------------------------------------------

# Two oversight roles together are ALLOWED (deliberately not an incompatible pair).
test_gov_two_oversight_roles_allowed if {
	d := bank.decision with input as gov_request(["BranchManager", "ComplianceOfficer"])
	d == permit
}

test_gov_single_teller_allowed if {
	d := bank.decision with input as gov_request(["Teller"])
	d == permit
}

test_gov_single_auditor_allowed if {
	d := bank.decision with input as gov_request(["Auditor"])
	d == permit
}

test_gov_empty_role_set_allowed if {
	d := bank.decision with input as gov_request([])
	d == permit
}

# Roles outside the fintech vocabulary are ignored — only the listed pairs matter.
test_gov_unknown_roles_ignored if {
	d := bank.decision with input as gov_request(["Teller", "SomeOtherRole"])
	d == permit
}

# ---------------------------------------------------------------------------
# Same-package guarantee: the governance decision is served from authz.bank, the fixed path the
# C# OpaDecisionProvider queries — so no adapter change is required.
# ---------------------------------------------------------------------------

test_gov_served_from_authz_bank_package if {
	d := data.authz.bank.decision with input as gov_request(["Auditor", "Teller"])
	d == deny_sod
}
