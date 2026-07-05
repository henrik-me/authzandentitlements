namespace AuthzEntitlements.Authz.Pdp.Providers.SpiceDb;

// The SpiceDB authorization schema, expressed in SpiceDB's native schema language. Held as a
// string so the bootstrap service pushes it verbatim via WriteSchema, and unit tests can assert
// its structure with no running server.
//
// This is the SpiceDB TRANSLATION of the OpenFGA ReBAC model
// (Providers/OpenFga/RebacModel.cs): the same four fintech relationship categories, so SpiceDB
// answers the SAME account questions as OpenFGA for the SHARED seed graph
// (Providers/OpenFga/RebacSeedTuples.cs). The head-to-head is only fair if both engines model the
// identical domain — this schema is verified to reproduce the OpenFGA forward-check catalog
// (RebacScenarioCatalog.Forward) exactly.
//
// Mapping from the OpenFGA model to SpiceDB:
//   * OpenFGA "relations" that are directly assigned (manager/region/branch/relationship_manager/
//     owner/customer/delegate) become SpiceDB `relation`s of the same name.
//   * OpenFGA computed "relations" (can_view/can_transact, and the branch/region manager
//     inheritance) become SpiceDB `permission`s. Where an OpenFGA relation was BOTH directly
//     assignable (`this`) AND computed, SpiceDB splits it into a base relation plus a permission —
//     the `viewer`/`transactor` relations carry the direct `this` self-grants (unused by the seed,
//     kept for faithfulness), and the permission unions them with the derived paths.
//   * OpenFGA tupleToUserset (e.g. account.can_view via branch->manager) becomes a SpiceDB arrow
//     (`branch->manage`); computedUserset (e.g. account.can_view via owner) becomes a direct
//     relation reference in the permission union.
//
// can_view composes the direct grant plus all four derived paths (viewer/owner/delegate/
// customer->can_view/branch->manage); can_transact is the tighter set (transactor/owner/
// customer->can_view) — exactly the OpenFGA model's two unions.
public static class SpiceDbSchema
{
    public const string Schema = """
    definition user {}

    definition region {
    	relation manager: user
    	permission manage = manager
    }

    definition branch {
    	relation region: region
    	relation manager: user
    	permission manage = manager + region->manage
    }

    definition customer {
    	relation branch: branch
    	relation relationship_manager: user
    	relation viewer: user
    	permission can_view = viewer + relationship_manager + branch->manage
    }

    definition account {
    	relation owner: user | customer
    	relation customer: customer
    	relation branch: branch
    	relation delegate: user
    	relation viewer: user
    	relation transactor: user
    	permission can_view = viewer + owner + delegate + customer->can_view + branch->manage
    	permission can_transact = transactor + owner + customer->can_view
    }
    """;
}
