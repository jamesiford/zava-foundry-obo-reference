using Compass.Agents.Contracts.Exceptions;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Exceptions;
using Compass.Bot.Application.Models;
using Compass.Bot.Application.Queries;
using Compass.Bot.Domain;
using Compass.Shared.Messaging;

namespace Compass.Bot.Host.Turns;

/// <summary>What to send back to the person for one turn.</summary>
/// <param name="Citations">Sources behind the answer, shown with it where the channel supports citations.</param>
public sealed record TurnReply(string Text, bool SignInRequired = false, IReadOnlyList<ReplyCitation>? Citations = null)
{
    public IReadOnlyList<ReplyCitation> Citations { get; } = Citations ?? [];
}

/// <summary>
/// Runs one Teams turn through <see cref="ISender"/> and translates every failure into a message the person can act on.
/// Kept apart from the Agents SDK glue so the fail-closed mapping is unit-testable without a channel.
/// </summary>
public sealed class BriefingTurnHandler(ISender sender, ILogger<BriefingTurnHandler> logger)
{
    public const string SignInMessage = "Please sign in again to continue.";

    public async Task<TurnReply> HandleAsync(string? ssoToken, string? question, CancellationToken cancellationToken, IBriefingProgressSink? progress = null)
    {
        // No token means no turn: there is no app-only fallback to answer with (contract rule 5).
        if (string.IsNullOrWhiteSpace(ssoToken))
        {
            return new TurnReply(SignInMessage, SignInRequired: true);
        }

        try
        {
            var briefing = await sender.SendAsync(new GetMemberBriefingQuery(new AccessToken(ssoToken), question ?? string.Empty, progress), cancellationToken);
            return new TurnReply(briefing.Answer, Citations: [.. BriefingCitations.From(briefing)]);
        }
        catch (Exception ex) when (IsAuthenticationFailure(ex))
        {
            // Type only: the message of an auth exception is token-free, but the type is all the log needs.
            logger.LogWarning("Turn failed closed with {ExceptionType}", ex.GetType().Name);
            return new TurnReply(SignInMessage, SignInRequired: true);
        }
        catch (MemberNotLinkedException)
        {
            logger.LogWarning("Turn stopped: signed-in person has no linked member record");
            return new TurnReply("I couldn't find a member record linked to your account.");
        }
        catch (HostedAgentException ex)
        {
            logger.LogError("Hosted agent {Agent} failed with HTTP {StatusCode}", ex.Agent, ex.StatusCode);
            return new TurnReply("One of the services I rely on is unavailable right now. Please try again shortly.");
        }
    }

    private static bool IsAuthenticationFailure(Exception ex) => BriefingFailure.IsAuthentication(ex);
}
