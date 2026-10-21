namespace Compass.Agents.Hosted.ZavaApi.Clients;

/// <summary>The MCP server ran the tool but reported a business error (for example an unknown member number).</summary>
public sealed class ZavaApiToolException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
