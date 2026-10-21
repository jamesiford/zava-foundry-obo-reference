namespace Compass.Mcp.ZavaApi.Domain;

/// <summary>
/// A deposit or loan account held by a <see cref="Member"/>.
/// </summary>
public sealed class Account
{
    public Account(
        string accountNumber,
        string memberNumber,
        AccountType type,
        string nickname,
        decimal balance,
        string currency,
        DateOnly openedOn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(nickname);
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        AccountNumber = accountNumber;
        MemberNumber = memberNumber;
        Type = type;
        Nickname = nickname;
        Balance = balance;
        Currency = currency;
        OpenedOn = openedOn;
    }

    public string AccountNumber { get; }

    /// <summary>Owning member. Held as a key, not a navigation, so the aggregate stays free of ORM concerns.</summary>
    public string MemberNumber { get; }

    public AccountType Type { get; }

    public string Nickname { get; }

    /// <summary>
    /// Ledger balance. Positive for deposit products; negative for loans, where the value is the amount still owed.
    /// </summary>
    public decimal Balance { get; }

    /// <summary>ISO 4217 currency code.</summary>
    public string Currency { get; }

    public DateOnly OpenedOn { get; }
}
