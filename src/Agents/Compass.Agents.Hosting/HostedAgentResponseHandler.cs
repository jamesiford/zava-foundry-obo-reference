using Azure.AI.AgentServer.Responses;
using Azure.AI.AgentServer.Responses.Models;
using Compass.Agents.Contracts;
using Compass.Agents.Contracts.Errors;
using Compass.Agents.Contracts.Responses;
using Microsoft.Extensions.Logging;

namespace Compass.Agents.Hosting;

/// <summary>
/// The one place a Compass agent meets the Foundry Responses hosting adapter (dev spec §9 hop 4): reads the typed request
/// from the user text, builds the call context from the forwarded headers, runs the agent, and puts the typed result in
/// the response text.
/// </summary>
/// <remarks>
/// Failed-closed identity errors are rethrown as a <see cref="ResponsesApiException"/> whose <c>code</c> is the
/// <see cref="AgentError"/> code and whose <c>message</c> carries the claims challenge (see <see cref="AgentErrors.PackMessage"/>).
/// The adapter turns that into a <c>status: "failed"</c> response that keeps exactly <c>code</c> and <c>message</c>
/// (observed: HTTP 200, <c>param</c> dropped), so the bot can tell a missing token from a rejected or challenged one. Any
/// other exception is deliberately left for the adapter to flatten to a generic <c>server_error</c> — internals never leak.
/// </remarks>
public abstract class HostedAgentResponseHandler<TRequest, TResponse>(
    IHostedAgent<TRequest, TResponse> agent,
    ILogger logger) : ResponseHandler
{
    public override IAsyncEnumerable<ResponseStreamEvent> CreateAsync(
        CreateResponse request,
        ResponseContext context,
        CancellationToken cancellationToken) =>
        new TextResponse(context, request, createText: ct => RunAsync(context, ct));

    private async Task<string> RunAsync(ResponseContext context, CancellationToken ct)
    {
        var callContext = AgentCallContextFactory.FromForwarded(context.ClientHeaders, context.PlatformContext);
        var inputText = await context.GetInputTextAsync(cancellationToken: ct);

        if (ResponsesWire.FromText<TRequest>(inputText) is not { } typedRequest)
        {
            throw new BadRequestException("The response request input must be the agent's JSON request.", "input");
        }

        // Ids only: the span and its baggage carry the agent's identity, never the person's, the request or the result.
        using var span = Agent365Telemetry.StartInvokeAgent(agent.GetType().Name);
        try
        {
            var result = await agent.HandleAsync(typedRequest, callContext, ct);
            return ResponsesWire.ToText(result);
        }
        catch (Exception ex) when (AgentErrors.FromException(ex) is { } mapped)
        {
            span.Fail(ex);
            // The message is token-free by construction; only the code and person are logged.
            logger.LogWarning("{Agent} failed closed for person {PersonObjectId}: {Code}", agent.GetType().Name, callContext.PersonObjectId, mapped.Error.Code);
            throw new ResponsesApiException(
                new Error(mapped.Error.Code, AgentErrors.PackMessage(mapped.Error)),
                mapped.StatusCode);
        }
        catch (Exception ex)
        {
            span.Fail(ex);
            throw;
        }
    }
}
