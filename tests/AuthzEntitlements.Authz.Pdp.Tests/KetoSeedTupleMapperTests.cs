using System.Text.Json;
using AuthzEntitlements.Authz.Pdp.Providers.Keto;
using AuthzEntitlements.Authz.Pdp.Providers.OpenFga;
using Xunit;

namespace AuthzEntitlements.Authz.Pdp.Tests;

// Pure OFFLINE guard for the Keto seed WRITE path: KetoSeedTupleMapper turns each shared RebacTuple
// into the exact JSON the adapter PUTs to Keto's /admin/relation-tuples. No client or server — the
// mapper is a pure function, which is exactly why the service factors it out (mirrors
// KetoRequestMapperTests).
//
// The load-bearing invariant these tests lock down (pinned against a live oryd/keto:v26.2.0): a
// subject-SET tuple must NOT emit a subject_id field at all. A PUT carrying BOTH an (empty)
// "subject_id" and a "subject_set" makes Keto store subject_id="" and silently DROP the subject_set,
// losing the structural relationship. So for every object-subject tuple we assert subject_id is
// ABSENT (not merely empty), and for every user-subject tuple we assert subject_set is absent.
public sealed class KetoSeedTupleMapperTests
{
    public static IEnumerable<object[]> SeedTupleData() =>
        RebacSeedTuples.Tuples.Select(t => new object[] { t.User, t.Relation, t.Object });

    // Every shared seed tuple round-trips to a well-formed Keto write body with EXACTLY ONE subject
    // field: a bare subject_id for a user subject, or a subject_set (with an empty relation) for an
    // object subject — and never both. The object side is always the (namespace, object, relation) triple.
    [Theory]
    [MemberData(nameof(SeedTupleData))]
    public void EverySeedTuple_MapsTo_ExactlyOneSubjectField(string user, string relation, string obj)
    {
        var json = KetoSeedTupleMapper.Serialize(KetoSeedTupleMapper.Map(new RebacTuple(user, relation, obj)));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Object side: always present and correct.
        var (objNamespace, objId) = SplitTypeId(obj);
        Assert.Equal(objNamespace, root.GetProperty("namespace").GetString());
        Assert.Equal(objId, root.GetProperty("object").GetString());
        Assert.Equal(relation, root.GetProperty("relation").GetString());

        if (user.StartsWith($"{RebacTypes.User}:", StringComparison.Ordinal))
        {
            // User subject -> bare subject_id, and NO subject_set.
            var (_, subjectId) = SplitTypeId(user);
            Assert.Equal(subjectId, root.GetProperty("subject_id").GetString());
            Assert.False(root.TryGetProperty("subject_set", out _));
        }
        else
        {
            // Object subject -> subject_set, and subject_id ABSENT (the exact invariant Keto requires:
            // an empty subject_id present alongside a subject_set makes Keto drop the subject_set).
            Assert.False(root.TryGetProperty("subject_id", out _));

            var subjectSet = root.GetProperty("subject_set");
            var (subjectNamespace, subjectId) = SplitTypeId(user);
            Assert.Equal(subjectNamespace, subjectSet.GetProperty("namespace").GetString());
            Assert.Equal(subjectId, subjectSet.GetProperty("object").GetString());
            Assert.Equal(string.Empty, subjectSet.GetProperty("relation").GetString());
        }
    }

    // Representative user-subject case: user:carol -> owner -> account:personal-carol. Full expected
    // shape: object side + a bare subject_id, with no subject_set.
    [Fact]
    public void UserSubjectTuple_Serializes_WithBareSubjectId_AndNoSubjectSet()
    {
        var json = KetoSeedTupleMapper.Serialize(
            KetoSeedTupleMapper.Map(new RebacTuple("user:carol", RebacRelations.Owner, "account:personal-carol")));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(RebacTypes.Account, root.GetProperty("namespace").GetString());
        Assert.Equal("personal-carol", root.GetProperty("object").GetString());
        Assert.Equal(RebacRelations.Owner, root.GetProperty("relation").GetString());
        Assert.Equal("carol", root.GetProperty("subject_id").GetString());
        Assert.False(root.TryGetProperty("subject_set", out _));
    }

    // Representative object-subject case: customer:acme -> owner -> account:acme-checking. Full expected
    // shape: object side + a subject_set with an empty relation, and subject_id ABSENT (the structural
    // tuple Keto would otherwise silently drop if subject_id were sent empty).
    [Fact]
    public void ObjectSubjectTuple_Serializes_WithSubjectSet_AndNoSubjectId()
    {
        var json = KetoSeedTupleMapper.Serialize(
            KetoSeedTupleMapper.Map(new RebacTuple("customer:acme", RebacRelations.Owner, "account:acme-checking")));

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(RebacTypes.Account, root.GetProperty("namespace").GetString());
        Assert.Equal("acme-checking", root.GetProperty("object").GetString());
        Assert.Equal(RebacRelations.Owner, root.GetProperty("relation").GetString());
        Assert.False(root.TryGetProperty("subject_id", out _));

        var subjectSet = root.GetProperty("subject_set");
        Assert.Equal(RebacTypes.Customer, subjectSet.GetProperty("namespace").GetString());
        Assert.Equal("acme", subjectSet.GetProperty("object").GetString());
        Assert.Equal(string.Empty, subjectSet.GetProperty("relation").GetString());
    }

    // A malformed "type:id" subject/object fails closed with a clear message rather than silently
    // mis-seeding (the ParseObject guard the mapper carries).
    [Theory]
    [InlineData("no-separator")]
    [InlineData(":missing-type")]
    [InlineData("missing-id:")]
    public void MalformedTypeId_FailsClosed(string malformed)
    {
        Assert.Throws<InvalidOperationException>(
            () => KetoSeedTupleMapper.Map(new RebacTuple(malformed, RebacRelations.Owner, "account:acme-checking")));
    }

    private static (string Namespace, string Id) SplitTypeId(string typeAndId)
    {
        var separator = typeAndId.IndexOf(':', StringComparison.Ordinal);
        return (typeAndId[..separator], typeAndId[(separator + 1)..]);
    }
}
