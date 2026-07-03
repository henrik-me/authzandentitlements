# CS08 — Unit tests for the OPA/Rego decision policy.
#
# The 22 tests named test_scenario_* mirror the FintechScenarioCatalog
# (../../src/AuthzEntitlements.Authz.Pdp/Catalog/FintechScenarioCatalog.cs)
# one-for-one — same inputs, asserting the exact decision + reason + obligations
# the reference provider returns. This is the OPA parity bar. The test_edge_*
# tests add fail-closed / boundary coverage beyond the catalog.
#
# Run: opa test infra/opa/policy -v

package authz.bank_test

import data.authz.bank

# ---------------------------------------------------------------------------
# Request builders (camelCase wire shape).
# ---------------------------------------------------------------------------

subject(id, roles, tenant) := {"type": "user", "id": id, "roles": roles, "tenant": tenant}

teller(id, tenant) := subject(id, ["Teller"], tenant)

manager(id, tenant) := subject(id, ["BranchManager"], tenant)

compliance(id, tenant) := subject(id, ["ComplianceOfficer"], tenant)

auditor(id, tenant) := subject(id, ["Auditor"], tenant)

account(tenant) := {"type": "account", "tenant": tenant}

tenant_resource(tenant) := {"type": "tenant", "tenant": tenant}

transaction(tenant, amount, maker_id) := {
	"type": "transaction",
	"tenant": tenant,
	"amount": amount,
	"makerId": maker_id,
}

transaction_status(tenant, amount, maker_id, status) := object.union(
	transaction(tenant, amount, maker_id),
	{"status": status},
)

request(subj, action, resource, scopes) := {
	"subject": subj,
	"action": {"name": action},
	"resource": resource,
	"context": {"scopes": scopes},
}

read_scope := ["bank.read"]

txn_write_scope := ["bank.transactions.write"]

approvals_scope := ["bank.approvals.write"]

no_scopes := []

# ---------------------------------------------------------------------------
# 22 parity scenarios (mirror FintechScenarioCatalog).
# ---------------------------------------------------------------------------

test_scenario_read_own_tenant_account if {
	d := bank.decision with input as request(teller("user-teller1", "CONTOSO"), "bank.account.read", account("CONTOSO"), read_scope)
	d == {"decision": "Permit", "reason": "Permit", "obligations": []}
}

test_scenario_read_other_tenant_account if {
	d := bank.decision with input as request(teller("user-teller1", "CONTOSO"), "bank.account.read", account("FABRIKAM"), read_scope)
	d == {"decision": "Deny", "reason": "TenantMismatch", "obligations": []}
}

test_scenario_auditor_reads_own_tenant if {
	d := bank.decision with input as request(auditor("user-auditor1", "CONTOSO"), "bank.account.read", tenant_resource("CONTOSO"), read_scope)
	d == {"decision": "Permit", "reason": "Permit", "obligations": []}
}

test_scenario_teller_create_account if {
	d := bank.decision with input as request(teller("user-teller1", "CONTOSO"), "bank.account.create", account("CONTOSO"), read_scope)
	d == {"decision": "Deny", "reason": "RoleNotAuthorized", "obligations": []}
}

test_scenario_manager_create_account_own_tenant if {
	d := bank.decision with input as request(manager("user-manager1", "CONTOSO"), "bank.account.create", account("CONTOSO"), read_scope)
	d == {"decision": "Permit", "reason": "Permit", "obligations": []}
}

test_scenario_manager_create_account_other_tenant if {
	d := bank.decision with input as request(manager("user-manager1", "CONTOSO"), "bank.account.create", account("FABRIKAM"), read_scope)
	d == {"decision": "Deny", "reason": "TenantMismatch", "obligations": []}
}

test_scenario_teller_create_small_txn if {
	d := bank.decision with input as request(teller("user-teller1", "CONTOSO"), "bank.transaction.create", transaction("CONTOSO", 250, "user-teller1"), txn_write_scope)
	d == {"decision": "Permit", "reason": "Permit", "obligations": ["post_immediately"]}
}

test_scenario_teller_create_large_txn if {
	d := bank.decision with input as request(teller("user-teller1", "CONTOSO"), "bank.transaction.create", transaction("CONTOSO", 15000, "user-teller1"), txn_write_scope)
	d == {"decision": "Permit", "reason": "Permit", "obligations": ["require_approval"]}
}

test_scenario_manager_approve_pending if {
	d := bank.decision with input as request(manager("user-manager1", "CONTOSO"), "bank.transaction.approve", transaction_status("CONTOSO", 15000, "user-teller1", "Pending"), approvals_scope)
	d == {"decision": "Permit", "reason": "Permit", "obligations": []}
}

test_scenario_teller_approve_not_eligible if {
	d := bank.decision with input as request(teller("user-teller1", "CONTOSO"), "bank.transaction.approve", transaction_status("CONTOSO", 15000, "user-manager1", "Pending"), approvals_scope)
	d == {"decision": "Deny", "reason": "RoleNotAuthorized", "obligations": []}
}

test_scenario_manager_approve_own_txn_sod if {
	d := bank.decision with input as request(manager("user-manager1", "CONTOSO"), "bank.transaction.approve", transaction_status("CONTOSO", 15000, "user-manager1", "Pending"), approvals_scope)
	d == {"decision": "Deny", "reason": "MakerEqualsChecker", "obligations": []}
}

test_scenario_compliance_reject_pending if {
	d := bank.decision with input as request(compliance("user-compliance1", "CONTOSO"), "bank.transaction.reject", transaction_status("CONTOSO", 15000, "user-teller1", "Pending"), approvals_scope)
	d == {"decision": "Permit", "reason": "Permit", "obligations": []}
}

test_scenario_manager_approve_already_approved if {
	d := bank.decision with input as request(manager("user-manager1", "CONTOSO"), "bank.transaction.approve", transaction_status("CONTOSO", 15000, "user-teller1", "Approved"), approvals_scope)
	d == {"decision": "Deny", "reason": "NotPending", "obligations": []}
}

test_scenario_manager_approve_other_tenant_txn if {
	d := bank.decision with input as request(manager("user-manager1", "CONTOSO"), "bank.transaction.approve", transaction_status("FABRIKAM", 15000, "user-teller1", "Pending"), approvals_scope)
	d == {"decision": "Deny", "reason": "TenantMismatch", "obligations": []}
}

test_scenario_fabrikam_teller_reads_contoso if {
	d := bank.decision with input as request(teller("user-fabrikam-teller", "FABRIKAM"), "bank.account.read", account("CONTOSO"), read_scope)
	d == {"decision": "Deny", "reason": "TenantMismatch", "obligations": []}
}

test_scenario_auditor_create_txn if {
	d := bank.decision with input as request(auditor("user-auditor1", "CONTOSO"), "bank.transaction.create", transaction("CONTOSO", 100, "user-auditor1"), txn_write_scope)
	d == {"decision": "Deny", "reason": "RoleNotAuthorized", "obligations": []}
}

test_scenario_teller_create_txn_no_scope if {
	d := bank.decision with input as request(teller("user-teller1", "CONTOSO"), "bank.transaction.create", transaction("CONTOSO", 250, "user-teller1"), no_scopes)
	d == {"decision": "Deny", "reason": "MissingScope", "obligations": []}
}

test_scenario_teller_read_no_scope if {
	d := bank.decision with input as request(teller("user-teller1", "CONTOSO"), "bank.account.read", account("CONTOSO"), no_scopes)
	d == {"decision": "Deny", "reason": "MissingScope", "obligations": []}
}

test_scenario_compliance_approve_own_txn_sod if {
	d := bank.decision with input as request(compliance("user-compliance1", "CONTOSO"), "bank.transaction.approve", transaction_status("CONTOSO", 20000, "user-compliance1", "Pending"), approvals_scope)
	d == {"decision": "Deny", "reason": "MakerEqualsChecker", "obligations": []}
}

test_scenario_teller_create_txn_for_other_maker if {
	d := bank.decision with input as request(teller("user-teller1", "CONTOSO"), "bank.transaction.create", transaction("CONTOSO", 250, "user-manager1"), txn_write_scope)
	d == {"decision": "Deny", "reason": "SubjectNotMaker", "obligations": []}
}

test_scenario_teller_create_threshold_boundary if {
	d := bank.decision with input as request(teller("user-teller1", "CONTOSO"), "bank.transaction.create", transaction("CONTOSO", 10000, "user-teller1"), txn_write_scope)
	d == {"decision": "Permit", "reason": "Permit", "obligations": ["require_approval"]}
}

test_scenario_unknown_action_fails_closed if {
	d := bank.decision with input as request(teller("user-teller1", "CONTOSO"), "bank.account.delete", account("CONTOSO"), read_scope)
	d == {"decision": "Deny", "reason": "UnknownAction", "obligations": []}
}

# ---------------------------------------------------------------------------
# Edge / fail-closed coverage beyond the catalog.
# ---------------------------------------------------------------------------

# Missing subject tenant → fail closed (TenantMismatch), even though scope passes.
test_edge_read_missing_subject_tenant if {
	subj := {"type": "user", "id": "user-teller1", "roles": ["Teller"]}
	d := bank.decision with input as request(subj, "bank.account.read", account("CONTOSO"), read_scope)
	d == {"decision": "Deny", "reason": "TenantMismatch", "obligations": []}
}

# Missing resource tenant → fail closed (TenantMismatch).
test_edge_read_missing_resource_tenant if {
	d := bank.decision with input as request(teller("user-teller1", "CONTOSO"), "bank.account.read", {"type": "account"}, read_scope)
	d == {"decision": "Deny", "reason": "TenantMismatch", "obligations": []}
}

# Whitespace-only tenant is not a real tenant → fail closed (TenantMismatch).
test_edge_read_blank_tenant if {
	d := bank.decision with input as request(teller("user-teller1", "   "), "bank.account.read", account("   "), read_scope)
	d == {"decision": "Deny", "reason": "TenantMismatch", "obligations": []}
}

# Approve without the approvals scope → MissingScope (first check).
test_edge_approve_missing_scope if {
	d := bank.decision with input as request(manager("user-manager1", "CONTOSO"), "bank.transaction.approve", transaction_status("CONTOSO", 15000, "user-teller1", "Pending"), no_scopes)
	d == {"decision": "Deny", "reason": "MissingScope", "obligations": []}
}

# account.create has NO scope check: a BranchManager with no scopes still permits.
test_edge_account_create_no_scope_still_permits if {
	d := bank.decision with input as request(manager("user-manager1", "CONTOSO"), "bank.account.create", account("CONTOSO"), no_scopes)
	d == {"decision": "Permit", "reason": "Permit", "obligations": []}
}

# Amount just below threshold → post_immediately.
test_edge_txn_just_below_threshold if {
	d := bank.decision with input as request(teller("user-teller1", "CONTOSO"), "bank.transaction.create", transaction("CONTOSO", 9999, "user-teller1"), txn_write_scope)
	d == {"decision": "Permit", "reason": "Permit", "obligations": ["post_immediately"]}
}

# Missing amount is treated as 0 → post_immediately.
test_edge_txn_missing_amount_defaults_zero if {
	resource := {"type": "transaction", "tenant": "CONTOSO", "makerId": "user-teller1"}
	d := bank.decision with input as request(teller("user-teller1", "CONTOSO"), "bank.transaction.create", resource, txn_write_scope)
	d == {"decision": "Permit", "reason": "Permit", "obligations": ["post_immediately"]}
}

# Another unrecognized verb also fails closed with UnknownAction.
test_edge_unknown_action_transaction_delete if {
	d := bank.decision with input as request(manager("user-manager1", "CONTOSO"), "bank.transaction.delete", account("CONTOSO"), approvals_scope)
	d == {"decision": "Deny", "reason": "UnknownAction", "obligations": []}
}
