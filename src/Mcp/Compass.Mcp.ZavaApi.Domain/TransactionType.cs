namespace Compass.Mcp.ZavaApi.Domain;

/// <summary>Business category of a posted transaction.</summary>
public enum TransactionType
{
    Deposit = 0,
    Withdrawal = 1,
    Transfer = 2,
    Payment = 3,
    Interest = 4,
    Fee = 5,
}
