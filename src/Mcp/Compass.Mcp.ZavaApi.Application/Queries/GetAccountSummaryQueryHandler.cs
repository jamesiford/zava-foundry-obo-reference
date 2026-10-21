using Compass.Mcp.ZavaApi.Application.Abstractions;
using Compass.Mcp.ZavaApi.Application.Exceptions;
using Compass.Shared.Messaging;
using Compass.Mcp.ZavaApi.Application.Models;

namespace Compass.Mcp.ZavaApi.Application.Queries;

public sealed class GetAccountSummaryQueryHandler(IMemberAccountRepository repository)
    : IRequestHandler<GetAccountSummaryQuery, MemberAccountsSummary>
{
    public async Task<MemberAccountsSummary> HandleAsync(GetAccountSummaryQuery request, CancellationToken cancellationToken)
    {
        // An unknown member must be a "not found", not an empty list: the two mean different things to the caller.
        _ = await repository.FindMemberAsync(request.MemberNumber, cancellationToken)
            ?? throw new MemberNotFoundException(request.MemberNumber);

        var accounts = await repository.ListAccountsAsync(request.MemberNumber, cancellationToken);

        return new MemberAccountsSummary(
            request.MemberNumber,
            [.. accounts.Select(AccountSummary.From)],
            accounts.Sum(a => a.Balance));
    }
}
