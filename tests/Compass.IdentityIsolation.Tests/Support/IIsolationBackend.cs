using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Contracts.Retrieval;

namespace Compass.IdentityIsolation.Tests.Support;

internal sealed record AskResult(RetrievalResult Documents, MemberBriefingResult Accounts, KnowledgeQueryResult Knowledge);

/// <summary>
/// The seam that lets one test body run against both the in-process fake backend (CI) and a live tenant. A test written
/// against this interface cannot reach into either implementation, so the live run asserts exactly what CI asserts.
/// </summary>
internal interface IIsolationBackend
{
    TestPerson UserA { get; }

    TestPerson UserB { get; }

    /// <summary>Runs one briefing as <paramref name="person"/>, signing them in however this backend does.</summary>
    Task<AskResult> AskAsync(TestPerson person, string question);
}
