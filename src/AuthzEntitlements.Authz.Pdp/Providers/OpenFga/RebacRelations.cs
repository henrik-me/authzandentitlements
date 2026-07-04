namespace AuthzEntitlements.Authz.Pdp.Providers.OpenFga;

// The ReBAC vocabulary CS07 shares across the model, seed tuples, request mapping, and
// scenario catalog so all four agree on the exact type and relation names. OpenFGA object
// ids are "type:id" strings and users are "user:id" strings; these constants are the single
// source of truth for the "type" and "relation" halves.
public static class RebacTypes
{
    public const string User = "user";
    public const string Region = "region";
    public const string Branch = "branch";
    public const string Customer = "customer";
    public const string Account = "account";
}

// The relation names used in the authorization model and tuples. can_view / can_transact are
// the two computed relations the PDP maps bank actions onto (forward Check); the rest are the
// direct/structural relations the seed tuples assign.
public static class RebacRelations
{
    public const string Manager = "manager";
    public const string Region = "region";
    public const string Branch = "branch";
    public const string RelationshipManager = "relationship_manager";
    public const string Owner = "owner";
    public const string Customer = "customer";
    public const string Delegate = "delegate";
    public const string CanView = "can_view";
    public const string CanTransact = "can_transact";
}

// The bank-action -> ReBAC-relation map the OpenFGA adapter uses to turn an AuthZEN
// AccessRequest into an OpenFGA Check. Kept engine-local (the reference RBAC/ABAC engine has
// no notion of these relations) and small; unknown actions fail closed at the mapper.
public static class RebacActionMap
{
    private static readonly IReadOnlyDictionary<string, string> ActionToRelation =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Contracts.ActionNames.AccountRead] = RebacRelations.CanView,
            [Contracts.ActionNames.TransactionCreate] = RebacRelations.CanTransact,
        };

    // True with the mapped relation when the action has a ReBAC relation; false for any action
    // outside the supported set (the caller then fails closed with UnknownAction).
    public static bool TryGetRelation(string action, out string relation) =>
        ActionToRelation.TryGetValue(action, out relation!);

    // The supported actions, exposed so tests can assert the map's shape without reflection.
    public static IReadOnlyCollection<string> SupportedActions => ActionToRelation.Keys.ToArray();
}
