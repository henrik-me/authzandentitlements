namespace AuthzEntitlements.Governance.Service.Metering;

// The kind of governance decision being recorded. Serialised kebab-lower in the audit
// event and the metric "type" tag so downstream ingestion (Audit.Service, CS13) sees
// stable, matchable values.
public enum GovernanceDecisionType
{
    Request,
    Approval,
    Rejection,
    Grant,
    Campaign,
    Review,
}

// The outcome of a governance decision. Multi-word members render kebab-case on the wire
// (e.g. SodDeny -> "sod-deny", GrantIssued -> "grant-issued").
public enum GovernanceOutcome
{
    Pending,
    Approved,
    Rejected,
    SodDeny,
    Unavailable,
    MakerCheckerDenied,
    ApproverNotEligible,
    GrantIssued,
    GrantRevoked,
    CampaignRun,
    ReviewDecided,
    Certified,
    Revoked,
    Completed,
}

// An audit-ready record of a single governance decision. Target is the access-package
// code, grant id, or campaign id the decision is about; CorrelationId ties the event back
// to the originating request/grant/campaign. Reason carries the SoD/maker-checker code
// when the outcome is a deny.
public sealed record GovernanceDecision(
    string TenantCode,
    string PrincipalId,
    GovernanceDecisionType DecisionType,
    string Target,
    GovernanceOutcome Outcome,
    string? Reason,
    string? CorrelationId,
    DateTimeOffset TimestampUtc);

// Renders the decision-type/outcome enums as stable, lower-cased kebab tokens. Kebab-case
// (not a bare ToLowerInvariant) so multi-word members keep readable, matchable boundaries
// — "grant-issued", not "grantissued".
public static class GovernanceWire
{
    public static string Token(GovernanceDecisionType type) => Kebab(type.ToString());

    public static string Token(GovernanceOutcome outcome) => Kebab(outcome.ToString());

    private static string Kebab(string pascalName)
    {
        var builder = new System.Text.StringBuilder(pascalName.Length + 4);
        for (var i = 0; i < pascalName.Length; i++)
        {
            var c = pascalName[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
