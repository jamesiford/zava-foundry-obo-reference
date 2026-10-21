using Compass.Mcp.ZavaApi.Domain;

namespace Compass.Mcp.ZavaApi.Infrastructure.Seeding;

/// <summary>
/// Deterministic, plausible credit-union data for the simulated core-banking store (dev spec D3, D11).
/// </summary>
/// <remarks>
/// Fully deterministic (no randomness) so tests and demos see the same members every run. Transaction dates are
/// expressed as "days before <c>asOfUtc</c>" so the ledger always looks recent. Balances are chosen for realism and
/// are not reconciled against the generated transactions — this is a demo fixture, not an accounting model.
/// </remarks>
public static class ZavaApiSeedData
{
    public const string Currency = "USD";

    private static readonly string[] Merchants =
    [
        "Debit card - Fresh Market", "Debit card - Shell Fuel", "Debit card - Corner Pharmacy",
        "Debit card - Trader Foods", "Debit card - City Hardware", "Debit card - Blue Bottle Coffee",
    ];

    private static readonly decimal[] CardAmounts = [84.12m, 46.30m, 23.99m, 112.47m, 61.08m, 18.75m];

    private static readonly MemberSeed[] Members =
    [
        new("ZCU-100234", "Daniel", "Park", "daniel.park@example.com", new(2014, 3, 11), MemberStatus.Active,
            "Northwind Logistics", 2315.40m, 1450.00m, "Rent payment", 18420.55m, 3187.22m, -14250.00m, 389.17m),
        new("ZCU-100871", "Maya", "Lin", "maya.lin@example.com", new(2019, 7, 2), MemberStatus.Active,
            "Contoso Health", 3120.00m, 1895.00m, "Mortgage payment", 52310.10m, 6402.88m, null, 0m),
        new("ZCU-101442", "Priya", "Raman", "priya.raman@example.com", new(2021, 1, 19), MemberStatus.Active,
            "Fabrikam Design", 2780.65m, 1620.00m, "Rent payment", 9275.00m, 1954.31m, -6800.00m, 244.50m),
        new("ZCU-102009", "Thomas", "Becker", "thomas.becker@example.com", new(2009, 10, 5), MemberStatus.Suspended,
            "Adatum Manufacturing", 2540.20m, 1310.00m, "Mortgage payment", 1204.77m, 212.40m, null, 0m),
        new("ZCU-102655", "Hannah", "Lindqvist", "hannah.lindqvist@example.com", new(2023, 5, 30), MemberStatus.Active,
            "Tailspin Education", 2105.90m, 1180.00m, "Rent payment", 4560.00m, 1033.65m, -9120.75m, 301.08m),
    ];

    public static SeedSet Create(DateTime asOfUtc)
    {
        if (asOfUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Seed reference time must be UTC.", nameof(asOfUtc));
        }

        var today = asOfUtc.Date;
        var members = new List<Member>();
        var accounts = new List<Account>();
        var transactions = new List<Transaction>();

        foreach (var seed in Members)
        {
            members.Add(new Member(seed.MemberNumber, seed.FirstName, seed.LastName, seed.Email, seed.JoinedOn, seed.Status));

            var digits = seed.MemberNumber[4..];
            var savings = Account(digits, "S01", seed, AccountType.ShareSavings, "Primary Share Savings", seed.SavingsBalance);
            var checking = Account(digits, "D02", seed, AccountType.ShareDraftChecking, "Everyday Checking", seed.CheckingBalance);
            accounts.Add(savings);
            accounts.Add(checking);

            transactions.AddRange(CheckingActivity(checking.AccountNumber, seed, today));
            transactions.AddRange(SavingsActivity(savings.AccountNumber, seed, today));

            if (seed.LoanBalance is { } loanBalance)
            {
                var loanType = seed.MemberNumber == "ZCU-102655" ? AccountType.PersonalLoan : AccountType.AutoLoan;
                var loanNickname = loanType == AccountType.AutoLoan ? "Auto Loan" : "Personal Loan";
                var loan = Account(digits, "L03", seed, loanType, loanNickname, loanBalance);
                accounts.Add(loan);
                transactions.AddRange(LoanActivity(loan.AccountNumber, seed, today));
            }
        }

        return new SeedSet(members, accounts, transactions);
    }

    private static Account Account(string digits, string suffix, MemberSeed seed, AccountType type, string nickname, decimal balance) =>
        new($"{digits}-{suffix}", seed.MemberNumber, type, nickname, balance, Currency, seed.JoinedOn.AddMonths(type == AccountType.ShareSavings ? 0 : 1));

    private static IEnumerable<Transaction> CheckingActivity(string accountNumber, MemberSeed seed, DateTime today)
    {
        var entries = new List<(int DaysAgo, string Description, decimal Amount, TransactionType Type)>();

        // Offset by the member's last digit so members don't all share the same card-spend pattern.
        var offset = seed.MemberNumber[^1] - '0';

        for (var k = 0; k < 6; k++)
        {
            var card = (k + offset) % Merchants.Length;
            entries.Add((3 + 14 * k, $"Payroll deposit - {seed.Employer}", seed.Payroll, TransactionType.Deposit));
            entries.Add((1 + 7 * k, Merchants[card], -CardAmounts[card], TransactionType.Withdrawal));
        }

        for (var m = 0; m < 3; m++)
        {
            entries.Add((5 + 30 * m, seed.HousingLabel, -seed.Housing, TransactionType.Payment));
            entries.Add((12 + 30 * m, "Utility bill - Metro Power & Water", -(96.40m + 7.15m * m), TransactionType.Payment));
        }

        return Number(accountNumber, entries, today);
    }

    private static IEnumerable<Transaction> SavingsActivity(string accountNumber, MemberSeed seed, DateTime today)
    {
        var entries = new List<(int DaysAgo, string Description, decimal Amount, TransactionType Type)>();
        var monthlyInterest = Math.Round(seed.SavingsBalance * 0.0035m / 12m, 2);

        for (var m = 0; m < 4; m++)
        {
            entries.Add((4 + 30 * m, "Transfer from Everyday Checking", 200.00m, TransactionType.Transfer));
            entries.Add((1 + 30 * m, "Dividend credit", monthlyInterest, TransactionType.Interest));
        }

        return Number(accountNumber, entries, today);
    }

    private static IEnumerable<Transaction> LoanActivity(string accountNumber, MemberSeed seed, DateTime today)
    {
        var entries = new List<(int DaysAgo, string Description, decimal Amount, TransactionType Type)>();

        for (var m = 0; m < 4; m++)
        {
            entries.Add((8 + 30 * m, "Loan payment", seed.LoanPayment, TransactionType.Payment));
        }

        entries.Add((95, "Late fee waived - courtesy credit", 25.00m, TransactionType.Fee));

        return Number(accountNumber, entries, today);
    }

    // IDs are numbered oldest-first so a higher sequence always means a later posting.
    private static IEnumerable<Transaction> Number(
        string accountNumber,
        IEnumerable<(int DaysAgo, string Description, decimal Amount, TransactionType Type)> entries,
        DateTime today)
    {
        var sequence = 1;
        foreach (var e in entries.OrderByDescending(e => e.DaysAgo))
        {
            yield return new Transaction(
                $"{accountNumber}-T{sequence++:D4}",
                accountNumber,
                DateTime.SpecifyKind(today.AddDays(-e.DaysAgo).AddHours(9 + sequence % 8), DateTimeKind.Utc),
                e.Description,
                e.Amount,
                e.Type);
        }
    }

    private sealed record MemberSeed(
        string MemberNumber,
        string FirstName,
        string LastName,
        string Email,
        DateOnly JoinedOn,
        MemberStatus Status,
        string Employer,
        decimal Payroll,
        decimal Housing,
        string HousingLabel,
        decimal SavingsBalance,
        decimal CheckingBalance,
        decimal? LoanBalance,
        decimal LoanPayment);
}

/// <summary>The full generated dataset, ready to insert.</summary>
public sealed record SeedSet(
    IReadOnlyList<Member> Members,
    IReadOnlyList<Account> Accounts,
    IReadOnlyList<Transaction> Transactions);
