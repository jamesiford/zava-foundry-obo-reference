using Compass.Mcp.ZavaApi.Application.Validation;

namespace Compass.Mcp.ZavaApi.Application.Queries;

public sealed class GetAccountSummaryQueryValidator : IRequestValidator<GetAccountSummaryQuery>
{
    public IReadOnlyList<string> Validate(GetAccountSummaryQuery request) =>
        [.. IdentifierRules.Check(nameof(request.MemberNumber), request.MemberNumber)];
}
