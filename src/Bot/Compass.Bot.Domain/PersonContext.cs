namespace Compass.Bot.Domain;

/// <summary>
/// The signed-in person for one turn. Resolved once from the validated Teams SSO token and then carried as a value,
/// never re-derived from a prompt, a tool argument or anything the person typed (contract rule 1).
/// </summary>
public sealed record PersonContext
{
    public PersonContext(string objectId, string tenantId, string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        ObjectId = objectId;
        TenantId = tenantId;
        DisplayName = displayName;
    }

    /// <summary>Entra <c>oid</c>: stable per person across the tenant, and the value Foundry's <c>x-ms-user-identity</c> expects.</summary>
    public string ObjectId { get; }

    public string TenantId { get; }

    public string? DisplayName { get; }
}
