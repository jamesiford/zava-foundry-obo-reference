using System.Net;
using System.Text.Json;
using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Contracts.Exceptions;
using Compass.Agents.Hosting;
using Compass.Shared.Identity.Tokens;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Compass.Agents.Hosted.ZavaApi.Clients;

/// <summary>
/// Talks to <c>Compass.Mcp.ZavaApi.Host</c> over the MCP streamable-HTTP transport, forwarding the person's
/// <c>x-client-zava-api-token</c> <em>unchanged</em> (brief §4, "Why this also fixes MCP"). It never exchanges or
/// re-mints the token: the MCP server validates the audience/issuer of exactly what the person's OBO exchange produced.
/// </summary>
/// <remarks>
/// A session is opened and closed per call. That keeps the token out of any long-lived connection state, at the cost
/// of an extra initialize round-trip — the right trade for an identity-sensitive path.
/// </remarks>
public sealed class ZavaApiMcpClient(Uri endpoint) : IZavaApiMcpClient
{
    public const string DownstreamName = "Zava Credit Union API";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<MemberBriefingResult> GetMemberBriefingAsync(string apiToken, string memberNumber, CancellationToken ct)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            // The raw token, no "Bearer" prefix: it travels in the x-client-* header the MCP middleware reads.
            AdditionalHeaders = new Dictionary<string, string> { [CompassHeaders.ZavaApiToken] = apiToken },
        };

        try
        {
            await using var client = await McpClient.CreateAsync(new HttpClientTransport(options), cancellationToken: ct);

            var member = await CallAsync<MemberPayload>(client, "get_member_summary", memberNumber, ct);
            var accounts = await CallAsync<AccountsPayload>(client, "get_account_summary", memberNumber, ct);

            return new MemberBriefingResult(
                member.MemberNumber,
                member.FullName,
                member.Status,
                accounts.Accounts.Select(a => new AccountBriefing(a.AccountNumber, a.Type, a.Nickname, a.Balance, a.Currency)).ToList(),
                accounts.NetBalance);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new DownstreamAuthenticationException(DownstreamName, (int)ex.StatusCode!.Value);
        }
    }

    private static async Task<T> CallAsync<T>(McpClient client, string tool, string memberNumber, CancellationToken ct)
    {
        using var span = Agent365Telemetry.StartExecuteTool(tool, "mcp");
        try
        {
            return await CallCoreAsync<T>(client, tool, memberNumber, ct);
        }
        catch (Exception ex)
        {
            span.Fail(ex);
            throw;
        }
    }

    private static async Task<T> CallCoreAsync<T>(McpClient client, string tool, string memberNumber, CancellationToken ct)
    {
        var result = await client.CallToolAsync(
            tool,
            new Dictionary<string, object?> { ["memberNumber"] = memberNumber },
            cancellationToken: ct);

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
            ?? throw new InvalidOperationException($"MCP tool '{tool}' returned no text content.");

        using var document = JsonDocument.Parse(text);
        if (document.RootElement.TryGetProperty("error", out var error))
        {
            throw new ZavaApiToolException(
                error.GetProperty("code").GetString() ?? "error",
                error.GetProperty("message").GetString() ?? "The Zava API reported an error.");
        }

        return document.RootElement.Deserialize<T>(Json)
            ?? throw new InvalidOperationException($"MCP tool '{tool}' returned an empty payload.");
    }

    private sealed record MemberPayload(string MemberNumber, string FullName, string Status);

    private sealed record AccountsPayload(List<AccountPayload> Accounts, decimal NetBalance);

    private sealed record AccountPayload(string AccountNumber, string Type, string Nickname, decimal Balance, string Currency);
}
