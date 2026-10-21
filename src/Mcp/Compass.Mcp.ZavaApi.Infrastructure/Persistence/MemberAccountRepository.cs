using Compass.Mcp.ZavaApi.Application.Abstractions;
using Compass.Mcp.ZavaApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace Compass.Mcp.ZavaApi.Infrastructure.Persistence;

/// <summary>
/// EF Core/SQLite implementation of the simulated core-banking adapter. Read-only and no-tracking: nothing here
/// can mutate the system of record.
/// </summary>
public sealed class MemberAccountRepository(ZavaApiDbContext db) : IMemberAccountRepository
{
    public Task<Member?> FindMemberAsync(string memberNumber, CancellationToken cancellationToken) =>
        db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.MemberNumber == memberNumber, cancellationToken);

    public async Task<IReadOnlyList<Account>> ListAccountsAsync(string memberNumber, CancellationToken cancellationToken) =>
        await db.Accounts.AsNoTracking()
            .Where(a => a.MemberNumber == memberNumber)
            .OrderBy(a => a.AccountNumber)
            .ToListAsync(cancellationToken);

    public Task<Account?> FindAccountAsync(string accountNumber, CancellationToken cancellationToken) =>
        db.Accounts.AsNoTracking().SingleOrDefaultAsync(a => a.AccountNumber == accountNumber, cancellationToken);

    public async Task<IReadOnlyList<Transaction>> ListRecentTransactionsAsync(string accountNumber, int count, CancellationToken cancellationToken) =>
        await db.Transactions.AsNoTracking()
            .Where(t => t.AccountNumber == accountNumber)
            .OrderByDescending(t => t.PostedAtUtc)
            .ThenByDescending(t => t.TransactionId)
            .Take(count)
            .ToListAsync(cancellationToken);
}
