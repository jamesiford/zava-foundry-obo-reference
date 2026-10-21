using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Hosted.ZavaApi.Clients;

namespace Compass.Agents.Contracts.Tests.Support;

/// <summary>Test double for <see cref="IZavaApiMcpClient"/>: records every call and returns or throws what the test sets up.</summary>
internal sealed class FakeZavaApiMcpClient : IZavaApiMcpClient
{
    public List<(string Token, string MemberNumber)> Calls { get; } = [];

    public MemberBriefingResult Result { get; set; } =
        new("ZCU-000001", "Test Member", "Active", [new AccountBriefing("000001-S01", "Savings", "Savings", 100m, "USD")], 100m);

    public Exception? Failure { get; set; }

    public Task<MemberBriefingResult> GetMemberBriefingAsync(string apiToken, string memberNumber, CancellationToken ct)
    {
        Calls.Add((apiToken, memberNumber));
        return Failure is null ? Task.FromResult(Result) : Task.FromException<MemberBriefingResult>(Failure);
    }
}
