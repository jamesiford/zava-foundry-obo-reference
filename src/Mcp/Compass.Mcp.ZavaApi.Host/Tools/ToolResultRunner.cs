using System.Text.Json;
using Compass.Mcp.ZavaApi.Application.Exceptions;
using Compass.Shared.Messaging;

namespace Compass.Mcp.ZavaApi.Host.Tools;

/// <summary>
/// Runs a use case and turns the outcome into the JSON text an MCP tool returns. Centralised so every tool reports
/// "not found" and "invalid input" identically instead of each re-implementing error mapping.
/// </summary>
public sealed class ToolResultRunner(ISender sender)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public async Task<string> RunAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await sender.SendAsync(request, cancellationToken);
            return JsonSerializer.Serialize(response, Json);
        }
        catch (ResourceNotFoundException ex)
        {
            return Error(ex.Code, ex.Message);
        }
        catch (RequestValidationException ex)
        {
            return Error("invalid_request", string.Join(" ", ex.Errors));
        }
    }

    private static string Error(string code, string message) =>
        JsonSerializer.Serialize(new { error = new { code, message } }, Json);
}
