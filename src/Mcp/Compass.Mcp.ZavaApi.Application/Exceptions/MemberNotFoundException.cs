namespace Compass.Mcp.ZavaApi.Application.Exceptions;

public sealed class MemberNotFoundException(string memberNumber)
    : ResourceNotFoundException("member_not_found", $"No member with number '{memberNumber}' was found.");
