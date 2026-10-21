using Compass.Mcp.ZavaApi.Application.Abstractions;
using Compass.Mcp.ZavaApi.Domain;
using Compass.Mcp.ZavaApi.Infrastructure.Seeding;
using Compass.Mcp.ZavaApi.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Compass.Mcp.ZavaApi.Tests.Seeding;

public sealed class ZavaApiSeedDataTests
{
    private static readonly DateTime AsOf = ZavaApiTestHost.SeedTime.UtcDateTime;

    [Fact]
    public void Seed_is_deterministic()
    {
        var first = ZavaApiSeedData.Create(AsOf);
        var second = ZavaApiSeedData.Create(AsOf);

        Assert.Equal(first.Transactions.Select(t => (t.TransactionId, t.PostedAtUtc, t.Amount)),
            second.Transactions.Select(t => (t.TransactionId, t.PostedAtUtc, t.Amount)));
    }

    [Fact]
    public void Seed_contains_every_account_type_the_domain_models()
    {
        var seed = ZavaApiSeedData.Create(AsOf);

        var seeded = seed.Accounts.Select(a => a.Type).Distinct().Order().ToArray();
        Assert.Contains(AccountType.ShareSavings, seeded);
        Assert.Contains(AccountType.ShareDraftChecking, seeded);
        Assert.Contains(AccountType.AutoLoan, seeded);
        Assert.Contains(AccountType.PersonalLoan, seeded);
    }

    [Fact]
    public void Seed_keys_are_unique_and_every_account_and_transaction_has_a_parent()
    {
        var seed = ZavaApiSeedData.Create(AsOf);

        Assert.Equal(seed.Members.Count, seed.Members.Select(m => m.MemberNumber).Distinct().Count());
        Assert.Equal(seed.Accounts.Count, seed.Accounts.Select(a => a.AccountNumber).Distinct().Count());
        Assert.Equal(seed.Transactions.Count, seed.Transactions.Select(t => t.TransactionId).Distinct().Count());

        var memberNumbers = seed.Members.Select(m => m.MemberNumber).ToHashSet();
        var accountNumbers = seed.Accounts.Select(a => a.AccountNumber).ToHashSet();
        Assert.All(seed.Accounts, a => Assert.Contains(a.MemberNumber, memberNumbers));
        Assert.All(seed.Transactions, t => Assert.Contains(t.AccountNumber, accountNumbers));
    }

    [Fact]
    public void Seed_postings_are_in_the_past_and_utc()
    {
        var seed = ZavaApiSeedData.Create(AsOf);

        Assert.All(seed.Transactions, t =>
        {
            Assert.Equal(DateTimeKind.Utc, t.PostedAtUtc.Kind);
            Assert.True(t.PostedAtUtc <= AsOf);
        });
    }

    [Fact]
    public void Seed_requires_a_utc_reference_time()
    {
        Assert.Throws<ArgumentException>(() => ZavaApiSeedData.Create(new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Local)));
    }

    [Fact]
    public async Task Initializing_twice_does_not_duplicate_the_seed()
    {
        await using var host = await ZavaApiTestHost.CreateAsync();
        var before = await host.Repository.ListAccountsAsync("ZCU-100234", CancellationToken.None);

        using var scope = host.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ZavaApiDatabaseInitializer>().InitializeAsync();

        var after = await scope.ServiceProvider.GetRequiredService<IMemberAccountRepository>()
            .ListAccountsAsync("ZCU-100234", CancellationToken.None);
        Assert.Equal(before.Count, after.Count);
    }

    [Fact]
    public async Task Transactions_read_back_with_utc_kind()
    {
        await using var host = await ZavaApiTestHost.CreateAsync();

        var transactions = await host.Repository.ListRecentTransactionsAsync("100234-D02", 5, CancellationToken.None);

        Assert.All(transactions, t => Assert.Equal(DateTimeKind.Utc, t.PostedAtUtc.Kind));
    }
}
