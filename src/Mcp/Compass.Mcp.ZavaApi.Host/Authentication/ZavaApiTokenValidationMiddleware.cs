using Compass.Shared.Identity.Tokens;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;
using Microsoft.Extensions.Logging;

namespace Compass.Mcp.ZavaApi.Host.Authentication;

/// <summary>
/// Gatekeeper for every tool invocation (dev spec §9, hop 6): validates audience and issuer of the
/// <c>x-client-zava-api-token</c> header before any handler runs.
/// </summary>
/// <remarks>
/// Fails closed — a missing context, missing header or invalid token stops the pipeline without calling the tool.
/// It must be registered after the MCP extension's own middleware, which is what publishes the invocation context
/// (and therefore the request headers) that this class reads.
/// </remarks>
public sealed class ZavaApiTokenValidationMiddleware(
    IAccessTokenValidator validator,
    ILogger<ZavaApiTokenValidationMiddleware> logger) : IFunctionsWorkerMiddleware
{
    // Key the MCP extension uses to publish the ToolInvocationContext; its constant is internal to the package.
    internal const string ToolInvocationContextKey = "ToolInvocationContext";

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var token = ReadToken(context);
        var result = await validator.ValidateAsync(token, context.CancellationToken);

        if (!result.IsValid)
        {
            logger.LogWarning("Rejected {Function}: token {Status}", context.FunctionDefinition.Name, result.Status);
            throw new ZavaApiUnauthorizedException($"Access token rejected: {result.Status}.");
        }

        // Correlate by the person's object ID so traces show who, without ever recording the token.
        using (logger.BeginScope(new Dictionary<string, object?> { ["UserObjectId"] = result.ObjectId }))
        {
            await next(context);
        }
    }

    private static string? ReadToken(FunctionContext context)
    {
        if (!context.Items.TryGetValue(ToolInvocationContextKey, out var value)
            || value is not ToolInvocationContext invocation
            || !invocation.TryGetHttpTransport(out var transport)
            || transport?.Headers is null)
        {
            return null;
        }

        // HTTP header names are case-insensitive but the transport hands back a plain dictionary.
        foreach (var (name, headerValue) in transport.Headers)
        {
            if (string.Equals(name, CompassHeaders.ZavaApiToken, StringComparison.OrdinalIgnoreCase))
            {
                return headerValue;
            }
        }

        return null;
    }
}
