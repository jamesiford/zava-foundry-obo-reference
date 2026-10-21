namespace Compass.Mcp.ZavaApi.Application.Exceptions;

public sealed class AccountNotFoundException(string accountNumber)
    : ResourceNotFoundException("account_not_found", $"No account with number '{accountNumber}' was found.");
