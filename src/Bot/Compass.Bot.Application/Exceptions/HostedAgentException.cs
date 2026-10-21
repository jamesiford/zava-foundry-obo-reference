using Compass.Bot.Application.Abstractions;

namespace Compass.Bot.Application.Exceptions;

/// <summary>A hosted agent failed for a reason that isn't about the person's token (outage, bad response).</summary>
public sealed class HostedAgentException(HostedAgentName agent, int statusCode)
    : Exception($"Hosted agent {agent} returned HTTP {statusCode}.")
{
    public HostedAgentName Agent { get; } = agent;

    public int StatusCode { get; } = statusCode;
}
