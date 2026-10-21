using Compass.Mcp.ZavaApi.Application.Abstractions;
using Compass.Mcp.ZavaApi.Application.Exceptions;
using Compass.Shared.Messaging;
using Compass.Mcp.ZavaApi.Application.Models;

namespace Compass.Mcp.ZavaApi.Application.Queries;

public sealed class GetMemberSummaryQueryHandler(IMemberAccountRepository repository)
    : IRequestHandler<GetMemberSummaryQuery, MemberSummary>
{
    public async Task<MemberSummary> HandleAsync(GetMemberSummaryQuery request, CancellationToken cancellationToken)
    {
        var member = await repository.FindMemberAsync(request.MemberNumber, cancellationToken)
            ?? throw new MemberNotFoundException(request.MemberNumber);

        return MemberSummary.From(member);
    }
}
