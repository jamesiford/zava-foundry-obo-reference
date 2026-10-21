using Compass.Mcp.ZavaApi.Application.Abstractions;
using Compass.Mcp.ZavaApi.Application.Exceptions;
using Compass.Shared.Messaging;
using Compass.Mcp.ZavaApi.Application.Models;

namespace Compass.Mcp.ZavaApi.Application.Queries;

public sealed class ListRecentTransactionsQueryHandler(IMemberAccountRepository repository)
    : IRequestHandler<ListRecentTransactionsQuery, RecentTransactions>
{
    public async Task<RecentTransactions> HandleAsync(ListRecentTransactionsQuery request, CancellationToken cancellationToken)
    {
        _ = await repository.FindAccountAsync(request.AccountNumber, cancellationToken)
            ?? throw new AccountNotFoundException(request.AccountNumber);

        var transactions = await repository.ListRecentTransactionsAsync(request.AccountNumber, request.Count, cancellationToken);

        return new RecentTransactions(request.AccountNumber, [.. transactions.Select(TransactionSummary.From)]);
    }
}
