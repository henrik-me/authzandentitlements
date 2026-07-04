namespace AuthzEntitlements.Authz.Pdp.Providers.OpenFga;

// A forward-Check ReBAC scenario: does UserId have Relation on ObjectType:ObjectId? Expressed
// with bare ids (no "user:"/"type:" prefixes) — the runner/service qualify them for OpenFGA.
public sealed record RebacForwardScenario(
    string Id,
    string Description,
    string UserId,
    string Relation,
    string ObjectType,
    string ObjectId,
    bool ExpectAllowed);

// A reverse-index "who can access object X" scenario (OpenFGA ListUsers): the set of users
// OpenFGA returns for (object, relation) must EXACTLY equal ExpectedUserIds — no missing and no
// extra — so the scenario is a precise oracle that catches both under- and over-granting.
public sealed record RebacWhoScenario(
    string Id,
    string Description,
    string ObjectType,
    string ObjectId,
    string Relation,
    IReadOnlyList<string> ExpectedUserIds);

// A reverse-index "what can user Y access" scenario (OpenFGA ListObjects): every id in
// ExpectedObjectIds must appear and every id in ExcludedObjectIds must NOT appear among the
// objects OpenFGA returns for (user, relation, objectType).
public sealed record RebacWhatScenario(
    string Id,
    string Description,
    string UserId,
    string Relation,
    string ObjectType,
    IReadOnlyList<string> ExpectedObjectIds,
    IReadOnlyList<string> ExcludedObjectIds);

// The CS07-specific ReBAC scenario catalog: engine-agnostic relationship questions with their
// expected outcomes, derived from RebacSeedTuples. Deliberately NOT the RBAC/ABAC
// FintechScenarioCatalog — ReBAC answers "who is related to what", not roles/scopes/maker-checker.
// Covers all four relationship types across forward Checks and both reverse-index directions,
// so it satisfies the exit criterion ("who can view account X" / "what can user Y access").
public static class RebacScenarioCatalog
{
    public static IReadOnlyList<RebacForwardScenario> Forward { get; } =
    [
        new("owner-can-view", "Account owner can view their own account.",
            "carol", RebacRelations.CanView, RebacTypes.Account, "personal-carol", true),
        new("delegate-can-view", "A delegate can view the account delegated to them.",
            "dave", RebacRelations.CanView, RebacTypes.Account, "personal-carol", true),
        new("rm-can-view-customer-account", "A relationship manager can view their customer's account.",
            "rm-anne", RebacRelations.CanView, RebacTypes.Account, "acme-checking", true),
        new("other-rm-cannot-view", "A different customer's RM cannot view the account.",
            "rm-bob", RebacRelations.CanView, RebacTypes.Account, "acme-checking", false),
        new("branch-manager-can-view", "The branch manager can view an account in their branch.",
            "branch-mgr-london", RebacRelations.CanView, RebacTypes.Account, "acme-checking", true),
        new("region-manager-can-view", "A region manager can view via the region -> branch hierarchy.",
            "region-mgr-emea", RebacRelations.CanView, RebacTypes.Account, "acme-checking", true),
        new("region-manager-other-region-cannot-view", "A region manager cannot view another region's account.",
            "region-mgr-emea", RebacRelations.CanView, RebacTypes.Account, "globex-ops", false),
        new("delegate-cannot-view-other-account", "A delegate has no relation to an unrelated account.",
            "dave", RebacRelations.CanView, RebacTypes.Account, "acme-checking", false),

        new("owner-can-transact", "The owner can transact on their own account.",
            "carol", RebacRelations.CanTransact, RebacTypes.Account, "personal-carol", true),
        new("delegate-cannot-transact", "A delegate can view but not transact (view-only delegation).",
            "dave", RebacRelations.CanTransact, RebacTypes.Account, "personal-carol", false),
        new("rm-can-transact-customer-account", "A relationship manager can transact on their customer's account.",
            "rm-anne", RebacRelations.CanTransact, RebacTypes.Account, "acme-checking", true),
        new("other-rm-cannot-transact", "A different customer's RM cannot transact.",
            "rm-bob", RebacRelations.CanTransact, RebacTypes.Account, "acme-checking", false),
    ];

    public static IReadOnlyList<RebacWhoScenario> WhoCanAccess { get; } =
    [
        new("who-can-view-acme-checking",
            "Who can view account:acme-checking (RM + branch + region managers).",
            RebacTypes.Account, "acme-checking", RebacRelations.CanView,
            ["rm-anne", "branch-mgr-london", "region-mgr-emea"]),
        new("who-can-view-personal-carol",
            "Who can view account:personal-carol: owner (carol) + delegate (dave) + the London branch " +
            "and EMEA region managers (personal-carol sits in branch:london).",
            RebacTypes.Account, "personal-carol", RebacRelations.CanView,
            ["carol", "dave", "branch-mgr-london", "region-mgr-emea"]),
    ];

    public static IReadOnlyList<RebacWhatScenario> WhatCanUserAccess { get; } =
    [
        new("what-can-region-mgr-emea-view",
            "What accounts can user:region-mgr-emea view (all EMEA-branch accounts, not AMER).",
            "region-mgr-emea", RebacRelations.CanView, RebacTypes.Account,
            ["acme-checking", "acme-savings", "personal-carol"],
            ["globex-ops"]),
        new("what-can-rm-anne-view",
            "What accounts can user:rm-anne view (only their customer's accounts).",
            "rm-anne", RebacRelations.CanView, RebacTypes.Account,
            ["acme-checking", "acme-savings"],
            ["globex-ops", "personal-carol"]),
    ];
}
