namespace Compass.Mcp.ZavaApi.Domain;

/// <summary>
/// A Zava Credit Union member: the person who owns accounts at the credit union, as opposed to the employee
/// (loan officer) calling the MCP server on their behalf.
/// </summary>
/// <remarks>
/// Kept distinct from the caller identity on purpose — the caller is established from the validated
/// <c>x-client-zava-api-token</c>, never from anything on this entity (Compass identity contract rule 1).
/// </remarks>
public sealed class Member
{
    public Member(
        string memberNumber,
        string firstName,
        string lastName,
        string email,
        DateOnly joinedOn,
        MemberStatus status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memberNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        MemberNumber = memberNumber;
        FirstName = firstName;
        LastName = lastName;
        Email = email;
        JoinedOn = joinedOn;
        Status = status;
    }

    /// <summary>The credit union's natural key for the member (e.g. <c>ZCU-100234</c>).</summary>
    public string MemberNumber { get; }

    public string FirstName { get; }

    public string LastName { get; }

    public string Email { get; }

    public DateOnly JoinedOn { get; }

    public MemberStatus Status { get; }

    public string FullName => $"{FirstName} {LastName}";
}
