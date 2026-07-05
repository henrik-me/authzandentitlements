using System.Text.Json;
using System.Text.Json.Serialization;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;

namespace AuthzEntitlements.Authz.Pdp.Providers.Keto;

// One Keto write-relationship body, shaped as the exact JSON that Keto's
// `PUT /admin/relation-tuples` expects. Held as a plain record (not the generated
// Ory.Keto.Client model) so the seed WRITE path controls the wire bytes explicitly and is
// offline-testable with System.Text.Json.
//
// WHY A HAND-ROLLED BODY: the generated KetoCreateRelationshipBody carries a non-nullable
// subject_id that its ToJson() renders as "subject_id": "" even for a subject-SET tuple. Against a
// live oryd/keto:v26.2.0 this is a proven data-loss hazard: a PUT whose JSON contains BOTH an
// (empty) "subject_id" AND a "subject_set" makes Keto STORE subject_id="" and silently DROP the
// subject_set — the structural tuple (e.g. customer:acme -> owner -> account:acme-checking) is lost.
// Correctness therefore hinges on subject_id being truly ABSENT (not empty) whenever a subject_set is
// present. Modelling subject_id as a nullable string and serializing with
// JsonIgnoreCondition.WhenWritingNull GUARANTEES that: a null subject field is OMITTED from the JSON
// entirely, so a subject-set tuple never carries a stray subject_id.
public sealed record KetoWriteRelationship(
    [property: JsonPropertyName("namespace")] string Namespace,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("relation")] string Relation,
    [property: JsonPropertyName("subject_id")] string? SubjectId,
    [property: JsonPropertyName("subject_set")] KetoWriteSubjectSet? SubjectSet);

// A Keto subject_set: a whole-object subject with an EMPTY relation, exactly how Zanzibar userset
// rewrites resolve (OPL `.traverse(...)` follows / `.includes(...)` matches a whole-object subject
// set). All three fields are non-null, so all three are always emitted — including "relation": "".
public sealed record KetoWriteSubjectSet(
    [property: JsonPropertyName("namespace")] string Namespace,
    [property: JsonPropertyName("object")] string Object,
    [property: JsonPropertyName("relation")] string Relation);

// The pure shared-seed-tuple -> Keto write-body mapping, factored out of KetoCheckService so it is
// unit testable with no client or server (the same "pure mapper factored out" pattern as
// KetoRequestMapper / SpiceDbRequestMapper). It turns each shared RebacTuple into the exact JSON the
// seed WRITE path PUTs to Keto, and is the single guardian of the subject_id-absent-for-subject-sets
// invariant the live investigation pinned down.
public static class KetoSeedTupleMapper
{
    // The one serializer configuration the seed WRITE path uses. WhenWritingNull is the load-bearing
    // setting: it OMITS a null subject field from the JSON, so a subject-set tuple never emits
    // "subject_id": "" (which Keto would honour by dropping the subject_set — see KetoWriteRelationship).
    // PRIVATE and FROZEN (Copilot review, PR #172; LRN-046 shared-JsonSerializerOptions freeze guidance):
    // no code — internal or external — can flip DefaultIgnoreCondition and reintroduce the hazard.
    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    // Maps a shared RebacTuple onto a Keto write body. The object always becomes the Keto
    // (namespace, object) pair; the subject is a bare subject_id when it is a user (`user:carol` ->
    // subject_id "carol") or a whole-object subject_set when it is another object
    // (`customer:acme`/`region:emea`/`branch:london` -> subject_set {namespace, object, relation=""}).
    // EXACTLY ONE subject field is ever populated: a subject_set tuple carries a null SubjectId (so it
    // is omitted on the wire), a user tuple carries a null SubjectSet — the two are never set together.
    public static KetoWriteRelationship Map(RebacTuple tuple)
    {
        var (objectNamespace, objectId) = ParseObject(tuple.Object);
        var (subjectType, subjectId) = ParseObject(tuple.User);

        if (string.Equals(subjectType, RebacTypes.User, StringComparison.Ordinal))
        {
            return new KetoWriteRelationship(
                Namespace: objectNamespace,
                Object: objectId,
                Relation: tuple.Relation,
                SubjectId: subjectId,
                SubjectSet: null);
        }

        return new KetoWriteRelationship(
            Namespace: objectNamespace,
            Object: objectId,
            Relation: tuple.Relation,
            SubjectId: null,
            SubjectSet: new KetoWriteSubjectSet(
                Namespace: subjectType,
                Object: subjectId,
                Relation: string.Empty));
    }

    // Serializes a write body with the shared options, so both the service and the offline guard tests
    // observe the identical wire JSON (in particular, the subject_id-omitted-for-subject-sets shape).
    public static string Serialize(KetoWriteRelationship r) => JsonSerializer.Serialize(r, SerializerOptions);

    // Splits a "type:id" ReBAC object string into its (type, id) halves. The seed strings always carry
    // a single ':' separator (e.g. "user:carol", "account:acme-checking"); a malformed string fails
    // closed with a clear message rather than silently mis-seeding.
    private static (string Type, string Id) ParseObject(string typeAndId)
    {
        var separator = typeAndId.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator == typeAndId.Length - 1)
        {
            throw new InvalidOperationException(
                $"Seed relationship object '{typeAndId}' is not a well-formed \"type:id\" string.");
        }

        return (typeAndId[..separator], typeAndId[(separator + 1)..]);
    }
}
