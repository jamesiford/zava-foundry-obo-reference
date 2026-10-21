using System.Text;
using System.Text.Json;

namespace Compass.IdentityIsolation.Tests.Support;

/// <summary>One server-sent event as the browser would parse it.</summary>
internal sealed record SseEvent(string Name, JsonElement Data);

internal static class Sse
{
    public static IReadOnlyList<SseEvent> Parse(string body) =>
        body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(block =>
            {
                var name = "message";
                var data = new StringBuilder();
                foreach (var line in block.Split('\n'))
                {
                    if (line.StartsWith("event: ", StringComparison.Ordinal))
                    {
                        name = line[7..];
                    }
                    else if (line.StartsWith("data: ", StringComparison.Ordinal))
                    {
                        data.Append(line[6..]);
                    }
                }

                return new SseEvent(name, JsonDocument.Parse(data.ToString()).RootElement.Clone());
            })
            .ToArray();

    public static HttpRequestMessage Post(string? token, string json = "{\"question\":\"What are my accounts?\"}", string contentType = "application/json")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/briefing")
        {
            Content = new StringContent(json, Encoding.UTF8, contentType),
        };
        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", token);
        }

        return request;
    }

    public static HttpRequestMessage Bearer(string token, string question = "What are my accounts?") =>
        Post("Bearer " + token, JsonSerializer.Serialize(new { question }));
}
