using Compass.Mcp.ZavaApi.Application.Validation;

namespace Compass.Mcp.ZavaApi.Application.Queries;

public sealed class GetMemberSummaryQueryValidator : IRequestValidator<GetMemberSummaryQuery>
{
    public IReadOnlyList<string> Validate(GetMemberSummaryQuery request) =>
        [.. IdentifierRules.Check(nameof(request.MemberNumber), request.MemberNumber)];
}
