using Compass.Agents.Contracts.Context;

namespace Compass.Agents.Contracts.Tests.Support;

internal static class TestContexts
{
    public static AgentCallContext With(string person, params (string Header, string Token)[] tokens) =>
        new(person, tokens.ToDictionary(t => t.Header, t => t.Token, StringComparer.OrdinalIgnoreCase));

    public static AgentCallContext Empty(string person = "person-oid") => With(person);
}
