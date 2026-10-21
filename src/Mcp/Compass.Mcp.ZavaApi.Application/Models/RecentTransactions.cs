namespace Compass.Mcp.ZavaApi.Application.Models;

/// <summary>Most-recent-first transactions for one account.</summary>
public sealed record RecentTransactions(string AccountNumber, IReadOnlyList<TransactionSummary> Transactions);
