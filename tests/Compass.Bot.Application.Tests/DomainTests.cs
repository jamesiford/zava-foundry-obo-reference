using Compass.Bot.Application.Tests.Support;
using Compass.Bot.Domain;

namespace Compass.Bot.Application.Tests;

public sealed class DomainTests
{
    [Fact]
    public void Access_token_never_reveals_its_value_when_formatted()
    {
        var raw = FakeTokens.Create("secret");
        var token = new AccessToken(raw);

        Assert.DoesNotContain(raw, token.ToString());
        Assert.DoesNotContain(raw, $"{token}");
        Assert.DoesNotContain(raw, token.GetHashCode().ToString());
        Assert.Equal(raw, token.Reveal());
    }

    [Fact]
    public void Access_token_record_formatting_is_redacted_too()
    {
        var raw = FakeTokens.Create("secret");

        Assert.DoesNotContain(raw, System.Text.Json.JsonSerializer.Serialize(new { Token = new AccessToken(raw).ToString() }));
    }

    [Fact]
    public void Token_set_rejects_a_second_token_for_the_same_audience()
    {
        var set = new DownstreamTokenSet();
        set.Add(DownstreamAudience.Graph, new AccessToken("a"));

        Assert.Throws<InvalidOperationException>(() => set.Add(DownstreamAudience.Graph, new AccessToken("b")));
    }

    [Fact]
    public void Session_id_is_stable_for_one_person()
    {
        Assert.Equal(SessionId.Mint(TestPeople.UserA), SessionId.Mint(new PersonContext(TestPeople.UserA.ObjectId, TestPeople.UserA.TenantId, "Other display name")));
    }

    [Fact]
    public void Session_ids_differ_between_people_and_between_tenants()
    {
        var other = new PersonContext(TestPeople.UserA.ObjectId, "tenant-2");

        Assert.NotEqual(SessionId.Mint(TestPeople.UserA), SessionId.Mint(TestPeople.UserB));
        Assert.NotEqual(SessionId.Mint(TestPeople.UserA), SessionId.Mint(other));
    }

    [Fact]
    public void Session_id_does_not_embed_the_object_id()
    {
        Assert.DoesNotContain(TestPeople.UserA.ObjectId, SessionId.Mint(TestPeople.UserA).Value);
    }

    [Theory]
    [InlineData("", "t")]
    [InlineData("o", " ")]
    public void Person_context_requires_object_and_tenant_ids(string oid, string tid)
    {
        Assert.ThrowsAny<ArgumentException>(() => new PersonContext(oid, tid));
    }
}
