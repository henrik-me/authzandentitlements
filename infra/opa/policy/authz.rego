# CS08 — OPA/Rego decision policy for the Authz PDP.
#
# This policy is the OPA adapter's engine. It mirrors the in-process
# ReferenceDecisionProvider
# (../../src/AuthzEntitlements.Authz.Pdp/Providers/ReferenceDecisionProvider.cs)
# EXACTLY: same coarse scopes, role eligibility, the 10,000 maker-checker
# threshold, tenant isolation, segregation of duties, and the fail-closed
# unknown-action path. The 22 FintechScenarioCatalog scenarios are the parity
# bar (see authz_test.rego).
#
# Contract (fixed — the C# OpaDecisionProvider depends on this shape):
#   Query:  POST {BaseUrl}/v1/data/authz/bank/decision  { "input": <request> }
#   Input:  camelCase AccessRequest wire shape —
#           input.subject.{type,id,roles[],tenant,branch}
#           input.action.name
#           input.resource.{type,id,tenant,branch,amount,makerId,status}
#           input.context.scopes[]
#   Output: { "decision": "Permit"|"Deny",
#             "reason":   one of Permit | MissingScope | TenantMismatch |
#                         RoleNotAuthorized | SubjectNotMaker |
#                         MakerEqualsChecker | NotPending | UnknownAction,
#             "obligations": [] | ["require_approval"] | ["post_immediately"] }
#
# `decision` is a TOTAL rule: a `default` covers every unrecognized action so
# the query always resolves to an object (fail closed on the unknown path).

package authz.bank

import rego.v1

# Mirrors ReferenceDecisionProvider.ApprovalThreshold / BankPolicy.ApprovalThreshold.
approval_threshold := 10000

# Mirrors AuthorizationSetup.MakerEligibleRoles — who may originate a transaction.
maker_eligible_roles := {"Teller", "BranchManager", "ComplianceOfficer"}

# Mirrors RoleNames.CheckerEligibleRoles — who may decide (check) an approval.
checker_eligible_roles := {"BranchManager", "ComplianceOfficer"}

# ---------------------------------------------------------------------------
# Entry point: total `decision` rule, dispatched by action name.
# ---------------------------------------------------------------------------

# Fail closed: any action outside the known vocabulary is denied, never permitted.
default decision := {"decision": "Deny", "reason": "UnknownAction", "obligations": []}

decision := read_decision if input.action.name == "bank.account.read"

decision := account_create_decision if input.action.name == "bank.account.create"

decision := transaction_create_decision if input.action.name == "bank.transaction.create"

decision := approval_decision if input.action.name == "bank.transaction.approve"

decision := approval_decision if input.action.name == "bank.transaction.reject"

# ---------------------------------------------------------------------------
# Per-action ordered checks. Each `else`-ladder returns the FIRST failing
# check's reason, exactly mirroring the reference provider's short-circuit.
# ---------------------------------------------------------------------------

# bank.account.read: read scope, then same-tenant. No role gate; resource.type
# is not checked (mirrors EvaluateRead).
read_decision := deny("MissingScope") if {
	not has_scope("bank.read")
} else := deny("TenantMismatch") if {
	not tenant_matches
} else := permit_no_obligation

# bank.account.create: BranchManager role, then same-tenant. NO scope check
# (mirrors EvaluateAccountCreate).
account_create_decision := deny("RoleNotAuthorized") if {
	not has_role("BranchManager")
} else := deny("TenantMismatch") if {
	not tenant_matches
} else := permit_no_obligation

# bank.transaction.create: write scope, maker-eligible role, subject-is-maker,
# then same-tenant; on permit carry the threshold obligation (mirrors
# EvaluateTransactionCreate).
transaction_create_decision := deny("MissingScope") if {
	not has_scope("bank.transactions.write")
} else := deny("RoleNotAuthorized") if {
	not has_any_role(maker_eligible_roles)
} else := deny("SubjectNotMaker") if {
	not subject_is_maker
} else := deny("TenantMismatch") if {
	not tenant_matches
} else := permit_with_obligation(transaction_obligation)

# bank.transaction.approve / reject: approvals scope, checker-eligible role,
# same-tenant, pending target, then segregation of duties. NotPending is checked
# BEFORE the SoD check so a self-approval of an already-decided transaction
# denies NotPending, not MakerEqualsChecker (mirrors EvaluateApprovalDecision).
approval_decision := deny("MissingScope") if {
	not has_scope("bank.approvals.write")
} else := deny("RoleNotAuthorized") if {
	not has_any_role(checker_eligible_roles)
} else := deny("TenantMismatch") if {
	not tenant_matches
} else := deny("NotPending") if {
	not is_pending
} else := deny("MakerEqualsChecker") if {
	subject_is_maker
} else := permit_no_obligation

# ---------------------------------------------------------------------------
# Helpers (small and readable, mirroring the reference provider predicates).
# ---------------------------------------------------------------------------

# A coarse scope is present in the request context.
has_scope(scope) if {
	some granted in input.context.scopes
	granted == scope
}

# The subject carries a specific role (exact, case-sensitive).
has_role(role) if {
	some held in input.subject.roles
	held == role
}

# The subject carries at least one role from an eligibility set.
has_any_role(eligible) if {
	some held in input.subject.roles
	held in eligible
}

# Tenant match fails closed: both sides present, non-blank, exactly equal.
tenant_matches if {
	is_non_blank(input.subject.tenant)
	is_non_blank(input.resource.tenant)
	input.subject.tenant == input.resource.tenant
}

# The caller acts as themselves: makerId present, non-empty, equal to subject id.
subject_is_maker if {
	is_string(input.resource.makerId)
	input.resource.makerId != ""
	input.subject.id == input.resource.makerId
}

# Only a "Pending" transaction may be approved or rejected.
is_pending if input.resource.status == "Pending"

# A value is a non-blank (non-whitespace) string.
is_non_blank(value) if {
	is_string(value)
	trim_space(value) != ""
}

# Threshold obligation on a permitted transaction.create: a missing amount is
# treated as 0 (mirrors `Amount ?? 0m`).
transaction_obligation := "require_approval" if {
	transaction_amount >= approval_threshold
} else := "post_immediately"

transaction_amount := amount if {
	amount := object.get(input.resource, "amount", 0)
	amount != null
} else := 0

# ---------------------------------------------------------------------------
# Decision constructors.
# ---------------------------------------------------------------------------

deny(reason) := {"decision": "Deny", "reason": reason, "obligations": []}

permit_no_obligation := {"decision": "Permit", "reason": "Permit", "obligations": []}

permit_with_obligation(obligation) := {
	"decision": "Permit",
	"reason": "Permit",
	"obligations": [obligation],
}
