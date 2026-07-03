namespace AuthzEntitlements.Authz.Pdp.Providers.OpenFga;

// The OpenFGA (ReBAC) authorization model, expressed as the canonical OpenFGA JSON API
// schema (schema 1.1). Held as a string so the bootstrap service deserializes it into the
// SDK's ClientWriteAuthorizationModelRequest verbatim, and unit tests can parse and assert
// its structure with no running server. The four fintech relationship types are:
//   * ownership        — account.owner (a user or a customer owns the account)
//   * RM -> customer    — customer.relationship_manager feeds customer.can_view, which flows
//                         to the customer's accounts via account.customer (indirection)
//   * branch/region     — branch.manager inherits region.manager (hierarchy), and both flow
//                         to accounts via account.branch and via customer.branch
//   * delegation        — account.delegate grants a specific user can_view on one account
// can_view composes all four (owner/delegate/can_view-from-customer/manager-from-branch);
// can_transact is the tighter set (direct/owner/can_view-from-customer).
public static class RebacModel
{
    public const string SchemaVersion = "1.1";

    // The authorization model in OpenFGA JSON API form. computedUserset/tupleToUserset use the
    // API's camelCase keys; type_definitions/schema_version/directly_related_user_types use the
    // snake_case keys — both exactly as the SDK model types deserialize.
    public const string Json = """
    {
      "schema_version": "1.1",
      "type_definitions": [
        { "type": "user" },
        {
          "type": "region",
          "relations": { "manager": { "this": {} } },
          "metadata": {
            "relations": {
              "manager": { "directly_related_user_types": [ { "type": "user" } ] }
            }
          }
        },
        {
          "type": "branch",
          "relations": {
            "region": { "this": {} },
            "manager": {
              "union": {
                "child": [
                  { "this": {} },
                  { "tupleToUserset": { "tupleset": { "relation": "region" }, "computedUserset": { "relation": "manager" } } }
                ]
              }
            }
          },
          "metadata": {
            "relations": {
              "region": { "directly_related_user_types": [ { "type": "region" } ] },
              "manager": { "directly_related_user_types": [ { "type": "user" } ] }
            }
          }
        },
        {
          "type": "customer",
          "relations": {
            "branch": { "this": {} },
            "relationship_manager": { "this": {} },
            "can_view": {
              "union": {
                "child": [
                  { "this": {} },
                  { "computedUserset": { "relation": "relationship_manager" } },
                  { "tupleToUserset": { "tupleset": { "relation": "branch" }, "computedUserset": { "relation": "manager" } } }
                ]
              }
            }
          },
          "metadata": {
            "relations": {
              "branch": { "directly_related_user_types": [ { "type": "branch" } ] },
              "relationship_manager": { "directly_related_user_types": [ { "type": "user" } ] },
              "can_view": { "directly_related_user_types": [ { "type": "user" } ] }
            }
          }
        },
        {
          "type": "account",
          "relations": {
            "owner": { "this": {} },
            "customer": { "this": {} },
            "branch": { "this": {} },
            "delegate": { "this": {} },
            "can_view": {
              "union": {
                "child": [
                  { "this": {} },
                  { "computedUserset": { "relation": "owner" } },
                  { "computedUserset": { "relation": "delegate" } },
                  { "tupleToUserset": { "tupleset": { "relation": "customer" }, "computedUserset": { "relation": "can_view" } } },
                  { "tupleToUserset": { "tupleset": { "relation": "branch" }, "computedUserset": { "relation": "manager" } } }
                ]
              }
            },
            "can_transact": {
              "union": {
                "child": [
                  { "this": {} },
                  { "computedUserset": { "relation": "owner" } },
                  { "tupleToUserset": { "tupleset": { "relation": "customer" }, "computedUserset": { "relation": "can_view" } } }
                ]
              }
            }
          },
          "metadata": {
            "relations": {
              "owner": { "directly_related_user_types": [ { "type": "user" }, { "type": "customer" } ] },
              "customer": { "directly_related_user_types": [ { "type": "customer" } ] },
              "branch": { "directly_related_user_types": [ { "type": "branch" } ] },
              "delegate": { "directly_related_user_types": [ { "type": "user" } ] },
              "can_view": { "directly_related_user_types": [ { "type": "user" } ] },
              "can_transact": { "directly_related_user_types": [ { "type": "user" } ] }
            }
          }
        }
      ]
    }
    """;
}
