using Compass.Bot.Host.Turns;
using Microsoft.Agents.Builder;
using Microsoft.Agents.Builder.App;
using Microsoft.Agents.Builder.State;
using Microsoft.Agents.Core.Models;

namespace Compass.Bot.Host;

/// <summary>
/// The Agents SDK entry point: pulls the person's Teams SSO token for the turn and hands the message to
/// <see cref="TeamsTurnResponder"/>. Nothing else lives here, so the identity flow stays in the Application layer.
/// </summary>
public sealed class CompassAgent : AgentApplication
{
    /// <summary>Name of the OAuth handler in <c>AgentApplication:UserAuthorization:Handlers</c> that performs Teams SSO.</summary>
    public const string SsoHandlerName = "teamsSso";

    private readonly IServiceScopeFactory _scopes;

    public CompassAgent(AgentApplicationOptions options, IServiceScopeFactory scopes)
        : base(options)
    {
        _scopes = scopes;
        OnActivity(ActivityTypes.Message, OnMessageAsync, autoSignInHandlers: [SsoHandlerName]);
    }

    private async Task OnMessageAsync(ITurnContext turnContext, ITurnState turnState, CancellationToken cancellationToken)
    {
        // The token is the platform's exchanged SSO token for the bot's own API; it identifies the person for this turn only.
        var ssoToken = await UserAuthorization.GetTurnTokenAsync(turnContext, SsoHandlerName, cancellationToken);

        // The agent itself lives for the process; the handler graph (and anything it holds) must not outlive one turn.
        await using var scope = _scopes.CreateAsyncScope();
        var responder = scope.ServiceProvider.GetRequiredService<TeamsTurnResponder>();
        await responder.RespondAsync(turnContext, ssoToken, cancellationToken);
    }
}
