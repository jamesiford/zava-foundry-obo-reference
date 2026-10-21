namespace Compass.Shared.Identity.Tokens;

/// <summary>
/// Names of the per-request headers that carry identity between hops (dev spec §8, §9). Centralised so a typo can
/// never silently turn into "no token" at a downstream hop.
/// </summary>
public static class CompassHeaders
{
    public const string UserIdentity = "x-ms-user-identity";

    public const string GraphAccessToken = "x-client-graph-access-token";

    public const string ZavaApiToken = "x-client-zava-api-token";

    /// <summary>
    /// The person's Azure AI Search token for the SharePoint knowledge agent. Unlike the other <c>x-client-*</c> tokens it
    /// is not sent as an HTTP header to a container: the prompt agent receives it as a Foundry structured input and
    /// Foundry binds it into the knowledge base MCP call's <c>x-ms-query-source-authorization</c> header.
    /// </summary>
    public const string SearchToken = "x-client-search-token";

    public const string AgentUserId = "x-agent-user-id";

    /// <summary>Set only by the bot: a per-person session identifier it minted itself (contract rule 8).</summary>
    public const string SessionId = "x-compass-session-id";
}
