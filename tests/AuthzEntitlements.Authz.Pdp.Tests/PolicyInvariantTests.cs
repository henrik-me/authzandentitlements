using AuthzEntitlements.Authz.Pdp.Contracts;
using AuthzEntitlements.Authz.Pdp.Providers;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Property-based policy invariants (CS17): rather than hand-picking scenarios, generate a broad,
// deterministic cross-product of requests and assert properties that must hold for EVERY input.
// These catch classes of regressions a fixed catalog can miss: non-total/throwing evaluation,
// permit/deny reason contract violations, missing threshold obligations, and — the strongest
// property — that every deterministic in-process RBAC engine answers identically ("same question,
// swappable engine"). The generator stays within the designed fintech vocabulary so agreement is
// a real invariant, not an accident.
public sealed class PolicyInvariantTests
{
    // A deterministic cross-product of requests over the fintech vocabulary. Deterministic (nested
    // loops, no RNG) so a failure is exactly reproducible.
    private static IReadOnlyList<AccessRequest> Generated()
    {
        string[] tenants = ["CONTOSO", "FABRIKAM"];
        (string Id, string Role)[] principals =
        [
            ("user-teller1", RoleNames.Teller),
            ("user-manager1", RoleNames.BranchManager),
            ("user-compliance1", RoleNames.ComplianceOfficer),
            ("user-auditor1", RoleNames.Auditor),
        ];
        string[] actions =
        [
            ActionNames.AccountRead,
            ActionNames.AccountCreate,
            ActionNames.TransactionCreate,
            ActionNames.TransactionApprove,
            ActionNames.TransactionReject,
            "bank.account.delete",
        ];
        decimal[] amounts = [100m, 9_999m, 10_000m, 25_000m];
        string?[] statuses = [null, "Pending", "Approved"];
        string[][] scopeSets =
        [
            [],
            [ScopeNames.Read],
            [ScopeNames.TransactionsWrite],
            [ScopeNames.ApprovalsWrite],
            [ScopeNames.Read, ScopeNames.TransactionsWrite, ScopeNames.ApprovalsWrite],
        ];

        var requests = new List<AccessRequest>();
        foreach (var (id, role) in principals)
        {
            foreach (var subjectTenant in tenants)
            {
                foreach (var action in actions)
                {
                    foreach (var resourceTenant in tenants)
                    {
                        foreach (var scopes in scopeSets)
                        {
                            var subject = new Subject("user", id, [role], subjectTenant);
                            var context = new EvaluationContext(scopes);

                            if (IsTransactionAction(action))
                            {
                                foreach (var amount in amounts)
                                {
                                    foreach (var status in statuses)
                                    {
                                        foreach (var makerId in new[] { id, "user-someone-else" })
                                        {
                                            requests.Add(new AccessRequest(
                                                subject,
                                                new ActionRequest(action),
                                                new Resource("transaction", "txn-1", resourceTenant, null, amount, makerId, status),
                                                context));
                                        }
                                    }
                                }
                            }
                            else
                            {
                                requests.Add(new AccessRequest(
                                    subject,
                                    new ActionRequest(action),
                                    new Resource("account", "acct-1", resourceTenant),
                                    context));
                            }
                        }
                    }
                }
            }
        }

        return requests;
    }

    private static bool IsTransactionAction(string action) =>
        action is ActionNames.TransactionCreate
            or ActionNames.TransactionApprove
            or ActionNames.TransactionReject;

    [Fact]
    public void Reference_IsTotalAndSelfExplaining_ForEveryRequest()
    {
        var reference = new ReferenceDecisionProvider();

        foreach (var request in Generated())
        {
            var decision = reference.Evaluate(request);

            Assert.NotEmpty(decision.Reasons);
            if (decision.Decision == Decision.Permit)
            {
                Assert.Equal(ReasonCodes.Permit, decision.Reasons[0].Code);
            }
            else
            {
                Assert.NotEqual(ReasonCodes.Permit, decision.Reasons[0].Code);
            }
        }
    }

    [Fact]
    public void Reference_IsDeterministic_ForEveryRequest()
    {
        var reference = new ReferenceDecisionProvider();

        foreach (var request in Generated())
        {
            var first = reference.Evaluate(request);
            var second = reference.Evaluate(request);

            Assert.Equal(first.Decision, second.Decision);
            Assert.Equal(first.Reasons[0].Code, second.Reasons[0].Code);
            Assert.Equal(
                first.Obligations.Select(o => o.Id).OrderBy(id => id, StringComparer.Ordinal),
                second.Obligations.Select(o => o.Id).OrderBy(id => id, StringComparer.Ordinal));
        }
    }

    [Fact]
    public void PermittedTransactionCreate_AlwaysCarriesExactlyOneThresholdObligation()
    {
        var reference = new ReferenceDecisionProvider();

        foreach (var request in Generated().Where(r => r.Action.Name == ActionNames.TransactionCreate))
        {
            var decision = reference.Evaluate(request);
            if (decision.Decision != Decision.Permit)
            {
                continue;
            }

            var obligation = Assert.Single(decision.Obligations);
            Assert.Contains(obligation.Id, new[] { ObligationIds.RequireApproval, ObligationIds.PostImmediately });

            var expected = (request.Resource.Amount ?? 0m) >= 10_000m
                ? ObligationIds.RequireApproval
                : ObligationIds.PostImmediately;
            Assert.Equal(expected, obligation.Id);
        }
    }

    [Fact]
    public void UnknownAction_AlwaysFailsClosed()
    {
        var reference = new ReferenceDecisionProvider();

        foreach (var request in Generated().Where(r => r.Action.Name == "bank.account.delete"))
        {
            var decision = reference.Evaluate(request);

            Assert.Equal(Decision.Deny, decision.Decision);
            Assert.Equal(ReasonCodes.UnknownAction, decision.Reasons[0].Code);
        }
    }

    [Fact]
    public void AllRbacEngines_AgreeOnDecisionAndPrimaryReason_ForEveryRequest()
    {
        var engines = LifecycleTestSupport.RbacProviders();
        var reference = engines[0];

        var mismatches = new List<string>();
        foreach (var request in Generated())
        {
            var baseline = reference.Evaluate(request);
            var baselineReason = baseline.Reasons[0].Code;

            foreach (var engine in engines.Skip(1))
            {
                var actual = engine.Evaluate(request);
                var actualReason = actual.Reasons.Count > 0 ? actual.Reasons[0].Code : "(none)";
                if (actual.Decision != baseline.Decision
                    || !string.Equals(actualReason, baselineReason, StringComparison.Ordinal))
                {
                    mismatches.Add(
                        $"{engine.Name} on [{request.Subject.Roles.FirstOrDefault()}/{request.Action.Name}/" +
                        $"amt={request.Resource.Amount}/status={request.Resource.Status}/scopes={request.Context.Scopes.Count}]: " +
                        $"{baseline.Decision}/{baselineReason} vs {actual.Decision}/{actualReason}");
                }
            }
        }

        Assert.True(mismatches.Count == 0,
            $"Cross-engine divergences ({mismatches.Count}):\n{string.Join("\n", mismatches.Take(20))}");
    }
}
