using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Compass.Bot.Host.FrontDoor;

/// <summary>
/// Writes server-sent events to one response. The status line and headers are held back until the first event, which is
/// what lets a failure that happens *before any progress* (a rejected token, a claims challenge) still be a real 401.
/// </summary>
/// <remarks>
/// Every write goes through one gate: progress arrives from the workflow's pump while the answer is written by the
/// request, and interleaved bytes would corrupt the stream. A terminal event also closes the writer, so a late progress
/// event can never appear after the answer.
/// </remarks>
public sealed class SseResponseWriter(HttpResponse response)
{
    public const string EndEvent = "end";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _closed;

    /// <summary>True once headers are on the wire; from then on the status can no longer change.</summary>
    public bool HasStarted { get; private set; }

    /// <summary>Writes one event unless the stream is already closed; returns whether it was written.</summary>
    public async Task<bool> WriteAsync(string eventName, object data, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_closed)
            {
                return false;
            }

            await WriteCoreAsync(eventName, data, cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Writes the final event and then <see cref="EndEvent"/>, and closes the writer.</summary>
    public async Task WriteTerminalAsync(string eventName, object data, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            await WriteCoreAsync(eventName, data, cancellationToken);
            await WriteCoreAsync(EndEvent, new { }, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WriteCoreAsync(string eventName, object data, CancellationToken cancellationToken)
    {
        if (!HasStarted)
        {
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "text/event-stream; charset=utf-8";
            response.Headers.CacheControl = "no-store";
            // Stops a reverse proxy from holding events back until the stream ends.
            response.Headers["X-Accel-Buffering"] = "no";
            response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            HasStarted = true;
        }

        // System.Text.Json never emits a raw newline without indentation, so one data line per event is safe.
        await response.WriteAsync($"event: {eventName}\ndata: {JsonSerializer.Serialize(data, Json)}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}
