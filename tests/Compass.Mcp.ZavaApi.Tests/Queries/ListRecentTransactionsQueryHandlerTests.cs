using Compass.Mcp.ZavaApi.Application.Exceptions;
using Compass.Mcp.ZavaApi.Application.Queries;
using Compass.Mcp.ZavaApi.Tests.Support;

namespace Compass.Mcp.ZavaApi.Tests.Queries;

public sealed class ListRecentTransactionsQueryHandlerTests : IAsyncLifetime
{
    private const string CheckingAccount = "100234-D02";

    private ZavaApiTestHost _host = null!;

    public async Task InitializeAsync() => _host = await ZavaApiTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Returns_the_default_number_of_transactions_newest_first()
    {
        var result = await _host.Sender.SendAsync(new ListRecentTransactionsQuery(CheckingAccount), CancellationToken.None);

        Assert.Equal(CheckingAccount, result.AccountNumber);
        Assert.Equal(ListRecentTransactionsQuery.DefaultCount, result.Transactions.Count);
        Assert.Equal(
            result.Transactions.OrderByDescending(t => t.PostedAtUtc).Select(t => t.TransactionId),
            result.Transactions.Select(t => t.TransactionId));
    }

    [Fact]
    public async Task Honours_the_requested_count()
    {
        var result = await _host.Sender.SendAsync(new ListRecentTransactionsQuery(CheckingAccount, 3), CancellationToken.None);

        Assert.Equal(3, result.Transactions.Count);
    }

    [Fact]
    public async Task Returns_all_transactions_when_fewer_exist_than_requested()
    {
        // The auto loan only has five postings in the seed data.
        var result = await _host.Sender.SendAsync(new ListRecentTransactionsQuery("100234-L03", 50), CancellationToken.None);

        Assert.Equal(5, result.Transactions.Count);
    }

    [Fact]
    public async Task Never_returns_postings_dated_in_the_future()
    {
        var result = await _host.Sender.SendAsync(new ListRecentTransactionsQuery(CheckingAccount, 50), CancellationToken.None);

        Assert.All(result.Transactions, t => Assert.True(t.PostedAtUtc <= ZavaApiTestHost.SeedTime.UtcDateTime));
    }

    [Fact]
    public async Task Only_returns_transactions_for_the_requested_account()
    {
        var result = await _host.Sender.SendAsync(new ListRecentTransactionsQuery("100234-S01", 50), CancellationToken.None);

        Assert.All(result.Transactions, t => Assert.StartsWith("100234-S01-", t.TransactionId));
    }

    [Fact]
    public async Task Throws_not_found_for_an_unknown_account()
    {
        var ex = await Assert.ThrowsAsync<AccountNotFoundException>(() =>
            _host.Sender.SendAsync(new ListRecentTransactionsQuery("000000-X99"), CancellationToken.None));

        Assert.Equal("account_not_found", ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(ListRecentTransactionsQuery.MaxCount + 1)]
    public async Task Rejects_counts_outside_the_allowed_range(int count)
    {
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            _host.Sender.SendAsync(new ListRecentTransactionsQuery(CheckingAccount, count), CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_a_missing_account_number()
    {
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            _host.Sender.SendAsync(new ListRecentTransactionsQuery(" "), CancellationToken.None));
    }
}
