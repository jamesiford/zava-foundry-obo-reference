namespace Compass.Mcp.ZavaApi.Domain;

/// <summary>
/// A posted entry against an <see cref="Account"/>.
/// </summary>
public sealed class Transaction
{
    public Transaction(
        string transactionId,
        string accountNumber,
        DateTime postedAtUtc,
        string description,
        decimal amount,
        TransactionType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (postedAtUtc.Kind != DateTimeKind.Utc)
        {
            // A local or unspecified kind would silently shift the ordering "most recent first" depends on.
            throw new ArgumentException("Posting time must be UTC.", nameof(postedAtUtc));
        }

        TransactionId = transactionId;
        AccountNumber = accountNumber;
        PostedAtUtc = postedAtUtc;
        Description = description;
        Amount = amount;
        Type = type;
    }

    public string TransactionId { get; }

    public string AccountNumber { get; }

    public DateTime PostedAtUtc { get; }

    public string Description { get; }

    /// <summary>Signed amount from the account's point of view: credits are positive, debits are negative.</summary>
    public decimal Amount { get; }

    public TransactionType Type { get; }
}
