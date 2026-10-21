using Compass.IdentityIsolation.Tests.Support;

namespace Compass.IdentityIsolation.Tests;

/// <summary>Cases 1 and 2 (dev spec §12). The Live variants run the same bodies against a real tenant.</summary>
public sealed class PersonIsolationTests
{
    [Fact]
    public async Task Fake_EachPersonSeesOnlyTheirOwnDocuments()
    {
        await using var backend = await FakeBackend.StartAsync();

        await IsolationCases.EachPersonSeesOnlyTheirOwnDocumentsAsync(backend);

        // Every Graph call was made with a token owned by the person the turn was for — never anonymous or shared.
        Assert.All(backend.Graph.Calls, c => Assert.NotNull(c.TokenOwnerObjectId));
    }

    [Fact]
    public async Task Fake_EachPersonSeesOnlyTheirOwnKnowledge()
    {
        await using var backend = await FakeBackend.StartAsync();

        await IsolationCases.EachPersonSeesOnlyTheirOwnKnowledgeAsync(backend);

        Assert.All(backend.Knowledge.Calls, c => Assert.NotNull(c.TokenOwnerObjectId));
    }

    [Fact]
    public async Task Fake_EachPersonGetsTheirOwnMemberData()
    {
        await using var backend = await FakeBackend.StartAsync();

        await IsolationCases.EachPersonGetsTheirOwnMemberDataAsync(backend);
    }

    [Fact]
    public async Task Fake_AgentsActOnTheCallersIdentityPerRequest()
    {
        await using var backend = await FakeBackend.StartAsync();
        var order = new[] { TestPerson.UserA, TestPerson.UserB, TestPerson.UserB, TestPerson.UserA };

        foreach (var person in order)
        {
            await backend.AskAsync(person, "anything");
        }

        // What the agent host saw on the wire, and whose token the downstream store resolved, both match the caller.
        var expected = order.Select(p => p.ObjectId).ToArray();
        Assert.Equal(expected, backend.Observations.Where(o => o.Agent == "ZavaApi").Select(o => o.PersonHeader));
        Assert.Equal(expected, backend.Observations.Where(o => o.Agent == "SharePointRetrieval").Select(o => o.PersonHeader));
        Assert.Equal(expected, backend.ZavaApi.Calls.Select(c => c.TokenOwnerObjectId));
        Assert.Equal(expected, backend.Graph.Calls.Select(c => c.TokenOwnerObjectId));
        Assert.Equal(expected, backend.Knowledge.Calls.Select(c => c.TokenOwnerObjectId));
        Assert.All(backend.Observations, o => Assert.Equal(200, o.StatusCode));
    }

    [Fact]
    public async Task Fake_EachDownstreamGetsATokenMintedForItsOwnAudience()
    {
        await using var backend = await FakeBackend.StartAsync();

        await backend.AskAsync(TestPerson.UserA, "anything");

        // The Graph token must not be the Zava API token or vice versa; the directory rejects a mismatched audience.
        var tokens = new[] { backend.Graph.Calls.Single().Token, backend.ZavaApi.Calls.Single().Token, backend.Knowledge.Calls.Single().Token };
        Assert.Equal(3, tokens.Distinct().Count());
        Assert.Equal(3, backend.Broker.Exchanges.Count);
    }

    [LiveFact]
    public async Task Live_EachPersonSeesOnlyTheirOwnDocuments()
    {
        using var backend = new LiveBackend();

        await IsolationCases.EachPersonSeesOnlyTheirOwnDocumentsAsync(backend);
    }

    [LiveFact]
    public async Task Live_EachPersonSeesOnlyTheirOwnKnowledge()
    {
        using var backend = new LiveBackend();

        await IsolationCases.EachPersonSeesOnlyTheirOwnKnowledgeAsync(backend);
    }

    [LiveFact]
    public async Task Live_EachPersonGetsTheirOwnMemberData()
    {
        using var backend = new LiveBackend();

        await IsolationCases.EachPersonGetsTheirOwnMemberDataAsync(backend);
    }
}
