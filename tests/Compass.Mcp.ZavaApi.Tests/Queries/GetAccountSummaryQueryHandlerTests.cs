using Compass.Mcp.ZavaApi.Application.Exceptions;
using Compass.Mcp.ZavaApi.Application.Queries;
using Compass.Mcp.ZavaApi.Tests.Support;

namespace Compass.Mcp.ZavaApi.Tests.Queries;

public sealed class GetAccountSummaryQueryHandlerTests : IAsyncLifetime
{
    private ZavaApiTestHost _host = null!;

    public async Task InitializeAsync() => _host = await ZavaApiTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Lists_every_account_and_nets_loans_against_deposits()
    {
        var summary = await _host.Sender.SendAsync(new GetAccountSummaryQuery("ZCU-100234"), CancellationToken.None);

        Assert.Equal("ZCU-100234", summary.MemberNumber);
        Assert.Equal(
            ["100234-D02", "100234-L03", "100234-S01"],
            summary.Accounts.Select(a => a.AccountNumber));
        Assert.Equal(18420.55m + 3187.22m - 14250.00m, summary.NetBalance);
    }

    [Fact]
    public async Task Shows_loan_balances_as_negative()
    {
        var summary = await _host.Sender.SendAsync(new GetAccountSummaryQuery("ZCU-100234"), CancellationToken.None);

        var loan = Assert.Single(summary.Accounts, a => a.Type == "AutoLoan");
        Assert.Equal(-14250.00m, loan.Balance);
    }

    [Fact]
    public async Task Returns_only_the_requested_members_accounts()
    {
        var summary = await _host.Sender.SendAsync(new GetAccountSummaryQuery("ZCU-100871"), CancellationToken.None);

        Assert.Equal(2, summary.Accounts.Count);
        Assert.All(summary.Accounts, a => Assert.StartsWith("100871-", a.AccountNumber));
    }

    [Fact]
    public async Task Throws_not_found_for_an_unknown_member()
    {
        await Assert.ThrowsAsync<MemberNotFoundException>(() =>
            _host.Sender.SendAsync(new GetAccountSummaryQuery("ZCU-999999"), CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_a_missing_member_number()
    {
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            _host.Sender.SendAsync(new GetAccountSummaryQuery(""), CancellationToken.None));
    }
}
