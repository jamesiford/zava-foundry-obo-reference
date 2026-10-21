using Compass.Mcp.ZavaApi.Domain;

namespace Compass.Mcp.ZavaApi.Application.Models;

public sealed record TransactionSummary(
    string TransactionId,
    DateTime PostedAtUtc,
    string Description,
    decimal Amount,
    string Type)
{
    public static TransactionSummary From(Transaction transaction) => new(
        transaction.TransactionId,
        transaction.PostedAtUtc,
        transaction.Description,
        transaction.Amount,
        transaction.Type.ToString());
}
