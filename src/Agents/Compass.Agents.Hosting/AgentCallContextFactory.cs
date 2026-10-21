using Azure.AI.AgentServer.Core;
using Compass.Agents.Contracts.Context;
using Compass.Shared.Identity.Tokens;

namespace Compass.Agents.Hosting;

/// <summary>
/// Builds the <see cref="AgentCallContext"/> from what the Foundry hosting adapter exposes to a handler (dev spec §9
/// hop 4): the forwarded <c>x-client-*</c> headers, and the platform-injected person identity.
/// </summary>
/// <remarks>
/// The platform drops <c>Authorization</c>, so the only credentials a handler can ever see are the <c>x-client-*</c>
/// headers the bot attached. Filtering on that prefix here means an unrelated forwarded header can never be mistaken for
/// a token.
/// </remarks>
public static class AgentCallContextFactory
{
    private const string ForwardedTokenPrefix = "x-client-";

    public static AgentCallContext FromForwarded(IReadOnlyDictionary<string, string> clientHeaders, PlatformContext platform)
    {
        ArgumentNullException.ThrowIfNull(clientHeaders);
        ArgumentNullException.ThrowIfNull(platform);

        var tokens = clientHeaders
            .Where(h => h.Key.StartsWith(ForwardedTokenPrefix, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(h.Value))
            .ToDictionary(h => h.Key.ToLowerInvariant(), h => h.Value, StringComparer.OrdinalIgnoreCase);

        return new AgentCallContext(ResolvePerson(clientHeaders, platform), tokens);
    }

    // The platform-injected id is authoritative; the identity header is only a fallback for local runs without the platform.
    private static string ResolvePerson(IReadOnlyDictionary<string, string> clientHeaders, PlatformContext platform)
    {
        if (!string.IsNullOrWhiteSpace(platform.UserIdKey))
        {
            return platform.UserIdKey;
        }

        foreach (var name in new[] { CompassHeaders.AgentUserId, CompassHeaders.UserIdentity })
        {
            if (clientHeaders.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }
}
