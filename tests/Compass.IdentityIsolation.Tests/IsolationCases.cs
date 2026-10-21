using Compass.IdentityIsolation.Tests.Support;

namespace Compass.IdentityIsolation.Tests;

/// <summary>
/// The isolation assertions themselves, written once against <see cref="IIsolationBackend"/> so the CI run (fake
/// backend) and the live run (real tenant) check exactly the same thing.
/// </summary>
internal static class IsolationCases
{
    private const string Question = "What should I know about my account and our policies?";

    /// <summary>Case 1: two people, both orders, each sees only the documents their own token can open.</summary>
    public static async Task EachPersonSeesOnlyTheirOwnDocumentsAsync(IIsolationBackend backend)
    {
        foreach (var (first, second) in new[] { (backend.UserA, backend.UserB), (backend.UserB, backend.UserA) })
        {
            // Back to back with the other person between, so any state kept from the previous caller would show.
            foreach (var person in new[] { first, second, first })
            {
                var result = await backend.AskAsync(person, Question);
                var text = string.Join('\n', result.Documents.Hits.Select(h => h.Text));

                Assert.Contains(person.Canary, text);
                Assert.DoesNotContain(person.ForeignCanary, text);
            }
        }
    }

    /// <summary>Case 1b: the Foundry IQ route to the same SharePoint content is scoped by the person's Search token, not by the agent.</summary>
    public static async Task EachPersonSeesOnlyTheirOwnKnowledgeAsync(IIsolationBackend backend)
    {
        foreach (var person in new[] { backend.UserA, backend.UserB, backend.UserB, backend.UserA })
        {
            var knowledge = (await backend.AskAsync(person, Question)).Knowledge;

            Assert.Contains(person.Canary, knowledge.Answer);
            Assert.DoesNotContain(person.ForeignCanary, knowledge.Answer);
        }
    }

    /// <summary>Case 2: the Zava API agent returns the signed-in person's own member data, never another member's.</summary>
    public static async Task EachPersonGetsTheirOwnMemberDataAsync(IIsolationBackend backend)
    {
        foreach (var person in new[] { backend.UserA, backend.UserB, backend.UserA })
        {
            var accounts = (await backend.AskAsync(person, Question)).Accounts;

            Assert.Equal(person.MemberNumber, accounts.MemberNumber);
            Assert.Equal(person.DisplayName, accounts.FullName);
            Assert.NotEqual(person.ForeignName, accounts.FullName);
        }
    }
}
