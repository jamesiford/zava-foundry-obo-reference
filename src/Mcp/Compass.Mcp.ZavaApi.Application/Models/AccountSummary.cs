using Compass.Mcp.ZavaApi.Domain;

namespace Compass.Mcp.ZavaApi.Application.Models;

public sealed record AccountSummary(
    string AccountNumber,
    string Type,
    string Nickname,
    decimal Balance,
    string Currency,
    DateOnly OpenedOn)
{
    public static AccountSummary From(Account account) => new(
        account.AccountNumber,
        account.Type.ToString(),
        account.Nickname,
        account.Balance,
        account.Currency,
        account.OpenedOn);
}
