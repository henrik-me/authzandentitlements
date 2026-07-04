namespace AuthzEntitlements.Authz.Pdp.Providers.OpenFga;

// One relationship tuple: user has relation on object, in OpenFGA "type:id" form. User is a
// full user string ("user:carol") or another object ("customer:acme", "region:emea") for the
// structural relations; Object is always "type:id".
public sealed record RebacTuple(string User, string Relation, string Object);

// The seed relationship graph for the CS07 ReBAC lab. Synthetic string ids (à la the
// FintechScenarioCatalog CONTOSO/FABRIKAM fixtures) keep it portable. The fixture covers all
// four relationship categories and is the ground truth the RebacScenarioCatalog expectations
// are derived from:
//   region managers  -> region:emea/amer
//   branch hierarchy  -> branch:london in region:emea, branch:nyc in region:amer
//   RM -> customer     -> rm-anne manages customer:acme, rm-bob manages customer:globex
//   account ownership  -> acme/globex accounts owned by their customer; personal-carol owned by a user
//   delegation         -> dave is a delegate on account:personal-carol
public static class RebacSeedTuples
{
    public static IReadOnlyList<RebacTuple> Tuples { get; } =
    [
        // Region managers (hierarchy roots).
        new("user:region-mgr-emea", RebacRelations.Manager, "region:emea"),
        new("user:region-mgr-amer", RebacRelations.Manager, "region:amer"),

        // Branch -> region hierarchy + branch managers.
        new("region:emea", RebacRelations.Region, "branch:london"),
        new("user:branch-mgr-london", RebacRelations.Manager, "branch:london"),
        new("region:amer", RebacRelations.Region, "branch:nyc"),

        // Customers: branch placement + relationship manager (RM -> customer).
        new("branch:london", RebacRelations.Branch, "customer:acme"),
        new("user:rm-anne", RebacRelations.RelationshipManager, "customer:acme"),
        new("branch:nyc", RebacRelations.Branch, "customer:globex"),
        new("user:rm-bob", RebacRelations.RelationshipManager, "customer:globex"),

        // Acme accounts: owned by the customer, linked to the customer (RM indirection) and branch.
        new("customer:acme", RebacRelations.Owner, "account:acme-checking"),
        new("customer:acme", RebacRelations.Customer, "account:acme-checking"),
        new("branch:london", RebacRelations.Branch, "account:acme-checking"),
        new("customer:acme", RebacRelations.Owner, "account:acme-savings"),
        new("customer:acme", RebacRelations.Customer, "account:acme-savings"),
        new("branch:london", RebacRelations.Branch, "account:acme-savings"),

        // Globex account (separate region/branch — used for cross-boundary deny scenarios).
        new("customer:globex", RebacRelations.Owner, "account:globex-ops"),
        new("customer:globex", RebacRelations.Customer, "account:globex-ops"),
        new("branch:nyc", RebacRelations.Branch, "account:globex-ops"),

        // Personal account: user ownership + delegation.
        new("user:carol", RebacRelations.Owner, "account:personal-carol"),
        new("branch:london", RebacRelations.Branch, "account:personal-carol"),
        new("user:dave", RebacRelations.Delegate, "account:personal-carol"),
    ];
}
