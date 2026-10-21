using Compass.Shared.Messaging;
using Compass.Mcp.ZavaApi.Application.Models;

namespace Compass.Mcp.ZavaApi.Application.Queries;

/// <summary>Last <paramref name="Count"/> transactions for an account, most recent first.</summary>
public sealed record ListRecentTransactionsQuery(string AccountNumber, int Count = ListRecentTransactionsQuery.DefaultCount)
    : IRequest<RecentTransactions>
{
    public const int DefaultCount = 10;

    /// <summary>Upper bound so a caller (or a model) can't request an unbounded dump of a ledger.</summary>
    public const int MaxCount = 100;
}
