using Compass.Mcp.ZavaApi.Application.Exceptions;
using Compass.Mcp.ZavaApi.Application.Queries;
using Compass.Mcp.ZavaApi.Tests.Support;

namespace Compass.Mcp.ZavaApi.Tests.Queries;

public sealed class GetMemberSummaryQueryHandlerTests : IAsyncLifetime
{
    private ZavaApiTestHost _host = null!;

    public async Task InitializeAsync() => _host = await ZavaApiTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Returns_the_profile_for_a_known_member()
    {
        var summary = await _host.Sender.SendAsync(new GetMemberSummaryQuery("ZCU-100234"), CancellationToken.None);

        Assert.Equal("ZCU-100234", summary.MemberNumber);
        Assert.Equal(        "Daniel Park", summary.FullName);
                Assert.Equal("daniel.park@example.com", summary.Email);
        Assert.Equal("Active", summary.Status);
        Assert.Equal(new DateOnly(2014, 3, 11), summary.JoinedOn);
    }

    [Fact]
    public async Task Reports_a_suspended_member_as_suspended()
    {
        var summary = await _host.Sender.SendAsync(new GetMemberSummaryQuery("ZCU-102009"), CancellationToken.None);

        Assert.Equal("Suspended", summary.Status);
    }

    [Fact]
    public async Task Throws_not_found_for_an_unknown_member()
    {
        var ex = await Assert.ThrowsAsync<MemberNotFoundException>(() =>
            _host.Sender.SendAsync(new GetMemberSummaryQuery("ZCU-999999"), CancellationToken.None));

        Assert.Equal("member_not_found", ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ZCU-1000000000000000000000000000000000000")]
    public async Task Rejects_malformed_member_numbers_before_touching_the_store(string memberNumber)
    {
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            _host.Sender.SendAsync(new GetMemberSummaryQuery(memberNumber), CancellationToken.None));
    }
}
