using Compass.Mcp.ZavaApi.Application.Validation;

namespace Compass.Mcp.ZavaApi.Application.Queries;

public sealed class ListRecentTransactionsQueryValidator : IRequestValidator<ListRecentTransactionsQuery>
{
    public IReadOnlyList<string> Validate(ListRecentTransactionsQuery request)
    {
        var errors = IdentifierRules.Check(nameof(request.AccountNumber), request.AccountNumber).ToList();

        if (request.Count is < 1 or > ListRecentTransactionsQuery.MaxCount)
        {
            errors.Add($"{nameof(request.Count)} must be between 1 and {ListRecentTransactionsQuery.MaxCount}.");
        }

        return errors;
    }
}
