namespace Compass.Bot.Infrastructure.Obo;

/// <summary>
/// Configuration for the bot's confidential client. There is deliberately no secret here: the credential is a federated
/// identity credential on the bot's app registration that trusts the bot's managed identity (dev spec §9 hop 2).
/// </summary>
public sealed class OnBehalfOfOptions
{
    public const string SectionName = "Bot:Obo";

    public string TenantId { get; set; } = string.Empty;

    /// <summary>The bot's Entra app registration (client) ID.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Client ID of the user-assigned managed identity trusted by the federated credential.</summary>
    public string ManagedIdentityClientId { get; set; } = string.Empty;

    /// <summary>Audience of the managed-identity token used as the client assertion.</summary>
    public string FederatedCredentialAudience { get; set; } = "api://AzureADTokenExchange";

    public string AuthorityHost { get; set; } = "https://login.microsoftonline.com";

    /// <summary>Delegated Graph scopes; <c>.default</c> picks up whatever was admin-consented (Files.Read.All, Sites.Read.All).</summary>
    public string[] GraphScopes { get; set; } = ["https://graph.microsoft.com/.default"];

    /// <summary>The Zava Credit Union API's scopes, e.g. <c>api://{zava-api-app-id}/.default</c>.</summary>
    public string[] ZavaApiScopes { get; set; } = [];

    /// <summary>Delegated Azure AI Search scope; a remote SharePoint knowledge source runs the query as the person with this token.</summary>
    public string[] SearchScopes { get; set; } = ["https://search.azure.com/.default"];
}
