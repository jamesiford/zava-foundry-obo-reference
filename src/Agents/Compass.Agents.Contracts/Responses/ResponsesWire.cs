using System.Text.Json;
using Compass.Agents.Contracts.Errors;

namespace Compass.Agents.Contracts.Responses;

/// <summary>
/// The single place that knows how a Compass agent's typed request and result ride inside the OpenAI Responses envelope
/// that Foundry speaks (dev spec §9 hops 3-4). The bot gateway and the hosted-agent adapter both go through it, so the
/// two ends cannot drift.
/// </summary>
/// <remarks>
/// The request is JSON in the user message text; the result is JSON in the assistant's <c>output_text</c>. Identity is
/// never carried here: it travels in headers (<c>x-ms-user-identity</c>, <c>x-client-*</c>), so nothing in this codec
/// ever touches a token.
/// </remarks>
public static class ResponsesWire
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    public static string ToText<T>(T value) => JsonSerializer.Serialize(value, Json);

    /// <summary>Parses agent output text; returns <c>default</c> when it is empty or not the expected JSON.</summary>
    public static T? FromText<T>(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(text, Json);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>Builds a create-response body, the way the SDK (and so Foundry) would send it, for tests and probes.</summary>
    public static string CreateRequestBody<T>(string agentName, T request) =>
        JsonSerializer.Serialize(
            new
            {
                agent_reference = new { type = "agent_reference", name = agentName },
                input = ToText(request),
            },
            Json);

    /// <summary>
    /// Extracts the user's text from a create-response body. Accepts <c>input</c> as a plain string or as the item
    /// array the SDK sends, and takes the last user message.
    /// </summary>
    public static bool TryReadInputText(JsonElement body, out string text)
    {
        text = string.Empty;
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("input", out var input))
        {
            return false;
        }

        if (input.ValueKind == JsonValueKind.String)
        {
            text = input.GetString() ?? string.Empty;
            return text.Length > 0;
        }

        if (input.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in input.EnumerateArray().Reverse())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                (item.TryGetProperty("role", out var role) && role.GetString() is not "user"))
            {
                continue;
            }

            if (!item.TryGetProperty("content", out var content))
            {
                continue;
            }

            var candidate = content.ValueKind == JsonValueKind.String
                ? content.GetString()
                : content.ValueKind == JsonValueKind.Array
                    ? string.Concat(content.EnumerateArray().Select(PartText))
                    : null;
            if (!string.IsNullOrEmpty(candidate))
            {
                text = candidate;
                return true;
            }
        }

        return false;
    }

    /// <summary>Builds a completed response envelope whose single assistant message carries <paramref name="outputText"/>.</summary>
    public static string CreateResponseBody(string outputText) =>
        JsonSerializer.Serialize(
            new
            {
                id = $"resp_{Guid.NewGuid():N}",
                @object = "response",
                created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                status = "completed",
                model = "compass-hosted-agent",
                output = new[]
                {
                    new
                    {
                        type = "message",
                        id = $"msg_{Guid.NewGuid():N}",
                        status = "completed",
                        role = "assistant",
                        content = new[] { new { type = "output_text", text = outputText, annotations = Array.Empty<object>() } },
                    },
                },
            },
            Json);

    /// <summary>Reads the assistant text back out of a response envelope (used by tests and probes; the bot uses the SDK).</summary>
    public static string? ReadOutputText(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return string.Concat(output.EnumerateArray()
            .Where(item => item.TryGetProperty("content", out _))
            .SelectMany(item => item.GetProperty("content").EnumerateArray())
            .Where(part => part.TryGetProperty("type", out var type) && type.GetString() == "output_text")
            .Select(part => part.GetProperty("text").GetString()));
    }

    /// <summary>
    /// Builds a failure body in the OpenAI error shape. The claims challenge is packed into <c>message</c> by
    /// <see cref="AgentErrors.PackMessage"/>: the Foundry hosting adapter keeps only <c>code</c> and <c>message</c> on a
    /// failure, so any other field would be lost before the bot sees it.
    /// </summary>
    public static string CreateErrorBody(AgentError error) =>
        JsonSerializer.Serialize(
            new { error = new { message = AgentErrors.PackMessage(error), type = "compass_agent_error", code = error.Code } },
            Json);

    /// <summary>
    /// Builds a <em>failed</em> response envelope — what the Foundry hosting adapter returns (HTTP 200,
    /// <c>status: "failed"</c>) when an agent throws, so tests can reproduce the real failure shape.
    /// </summary>
    public static string CreateFailedResponseBody(AgentError error) =>
        JsonSerializer.Serialize(
            new
            {
                id = $"resp_{Guid.NewGuid():N}",
                @object = "response",
                created_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                status = "failed",
                model = "compass-hosted-agent",
                error = new { code = error.Code, message = AgentErrors.PackMessage(error) },
                output = Array.Empty<object>(),
            },
            Json);

    /// <summary>Reads a failure body back into an <see cref="AgentError"/>; <c>null</c> when it isn't an agent error.</summary>
    public static AgentError? ReadError(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("error", out var error) ||
                error.ValueKind != JsonValueKind.Object ||
                StringOf(error, "code") is not { Length: > 0 } code)
            {
                return null;
            }

            return AgentErrors.Unpack(code, StringOf(error, "message"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? StringOf(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string PartText(JsonElement part) =>
        part.ValueKind == JsonValueKind.Object && part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString() ?? string.Empty
            : string.Empty;
}
