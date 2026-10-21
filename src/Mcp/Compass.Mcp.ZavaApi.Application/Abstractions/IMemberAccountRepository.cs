using Compass.Mcp.ZavaApi.Domain;

namespace Compass.Mcp.ZavaApi.Application.Abstractions;

/// <summary>
/// The boundary to the system of record. Application code depends on this port only, so swapping the simulated
/// SQLite store for a real core-banking adapter is a DI change (dev spec D3, §6 Dependency Inversion).
/// </summary>
/// <remarks>
/// Every method is read-only: this MCP server never moves money. Results are never filtered by caller here —
/// the token was validated before any handler ran.
/// </remarks>
public interface IMemberAccountRepository
{
    Task<Member?> FindMemberAsync(string memberNumber, CancellationToken cancellationToken);

    Task<IReadOnlyList<Account>> ListAccountsAsync(string memberNumber, CancellationToken cancellationToken);

    Task<Account?> FindAccountAsync(string accountNumber, CancellationToken cancellationToken);

    /// <summary>Returns up to <paramref name="count"/> transactions, most recent first.</summary>
    Task<IReadOnlyList<Transaction>> ListRecentTransactionsAsync(string accountNumber, int count, CancellationToken cancellationToken);
}
