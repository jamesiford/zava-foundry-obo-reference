using System.Text.Json;
using Compass.Bot.Application.Models;
using Compass.Bot.Application.Queries;
using Compass.Bot.Domain;
using Compass.Bot.Host.Turns;
using Compass.Shared.Messaging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Compass.Bot.Host.FrontDoor;

/// <summary>The only thing a caller may send: the question. Everything else about who is asking comes from the bearer token.</summary>
public sealed record BriefingRequest(string? Question);

/// <summary>The composed briefing as the browser receives it.</summary>
public sealed record AnswerEvent(string? DisplayName, string Answer, IReadOnlyList<CitationEvent> Citations, SharePointRoute Route);

/// <summary>One source behind the answer; <see cref="Url"/> is only ever an http(s) link.</summary>
public sealed record CitationEvent(string Title, string? Url);

/// <summary>A typed failure after the stream has started. Carries a code (and a claims request), never exception text.</summary>
public sealed record ErrorEvent(string Code, string? Claims);

/// <summary>
/// <c>POST /api/briefing</c>: the web front door onto the same <see cref="GetMemberBriefingQuery"/> Teams uses.
/// </summary>
/// <remarks>
/// The bearer token is passed to the query unchanged, so the person is resolved exactly once, inside the query handler,
/// by the same validator and OBO broker as Teams. This class deliberately never inspects the token and never reads a
/// person, tenant, member or session id from the request: the body type has a single property and unknown properties are
/// rejected rather than ignored.
/// </remarks>
public sealed class BriefingHandler(
    ISender sender,
    IOptions<FrontDoorOptions> options,
    ILogger<BriefingHandler> logger)
{
    public const string AnswerEventName = "answer";
    public const string ErrorEventName = "error";

    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    public async Task HandleAsync(HttpContext context)
    {
        var cancellationToken = context.RequestAborted;
        var settings = options.Value;
        context.Response.Headers.CacheControl = "no-store";

        var token = ReadBearerToken(context.Request);
        if (token is null)
        {
            await RejectAsync(context, new FrontDoorFailure(StatusCodes.Status401Unauthorized, FrontDoorFailure.SignInRequired), "Bearer");
            return;
        }

        var question = await ReadQuestionAsync(context.Request, settings, cancellationToken);
        if (question.Failure is { } invalid)
        {
            await RejectAsync(context, invalid);
            return;
        }

        var writer = new SseResponseWriter(context.Response);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(settings.TurnDeadline);

        MemberBriefing briefing;
        try
        {
            briefing = await sender.SendAsync(new GetMemberBriefingQuery(new AccessToken(token), question.Text!, new SseProgressSink(writer)), deadline.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The browser went away; there is nobody to answer.
            return;
        }
        catch (Exception ex)
        {
            await FailAsync(context, writer, ex, timedOut: deadline.IsCancellationRequested, cancellationToken);
            return;
        }

        var answer = new AnswerEvent(
            briefing.Person.DisplayName,
            briefing.Answer,
            [.. BriefingCitations.From(briefing).Select(c => new CitationEvent(c.Title, c.Url))],
            briefing.Route);
        try
        {
            await writer.WriteTerminalAsync(AnswerEventName, answer, cancellationToken);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            logger.LogInformation("Front door client disconnected before the answer was written");
        }
    }

    private async Task FailAsync(HttpContext context, SseResponseWriter writer, Exception exception, bool timedOut, CancellationToken cancellationToken)
    {
        var failure = FrontDoorFailure.From(exception, timedOut);

        // Type only: exception messages from token libraries can echo claim values.
        logger.LogWarning("Front door briefing failed closed with {ExceptionType} ({Code})", exception.GetType().Name, failure.Code);

        try
        {
            if (writer.HasStarted)
            {
                await writer.WriteTerminalAsync(ErrorEventName, new ErrorEvent(failure.Code, failure.Claims), cancellationToken);
            }
            else
            {
                await RejectAsync(context, failure, failure.Challenge);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            logger.LogInformation("Front door client disconnected before the error was written");
        }
    }

    private static async Task RejectAsync(HttpContext context, FrontDoorFailure failure, string? challenge = null)
    {
        context.Response.StatusCode = failure.Status;
        if (challenge is not null)
        {
            context.Response.Headers.WWWAuthenticate = challenge;
        }

        await context.Response.WriteAsJsonAsync(new { error = failure.Code }, context.RequestAborted);
    }

    private static string? ReadBearerToken(HttpRequest request)
    {
        const string Prefix = "Bearer ";
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = header[Prefix.Length..].Trim();
        return token.Length is > 0 and <= MaxTokenLength ? token : null;
    }

    private const int MaxTokenLength = 16 * 1024;

    private static async Task<(string? Text, FrontDoorFailure? Failure)> ReadQuestionAsync(HttpRequest request, FrontDoorOptions settings, CancellationToken cancellationToken)
    {
        var invalid = new FrontDoorFailure(StatusCodes.Status400BadRequest, FrontDoorFailure.InvalidRequest);

        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType) || !mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return (null, new FrontDoorFailure(StatusCodes.Status415UnsupportedMediaType, FrontDoorFailure.InvalidRequest));
        }

        var body = await ReadBoundedAsync(request.Body, settings.MaxBodyBytes, cancellationToken);
        if (body is null)
        {
            return (null, new FrontDoorFailure(StatusCodes.Status413PayloadTooLarge, FrontDoorFailure.InvalidRequest));
        }

        BriefingRequest? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<BriefingRequest>(body.Value.Span, RequestJson);
        }
        catch (JsonException)
        {
            return (null, invalid);
        }

        var text = parsed?.Question?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > settings.MaxQuestionLength || text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
        {
            return (null, invalid);
        }

        return (text, null);
    }

    // Reads at most limit bytes; null means the body was larger, so a hostile caller cannot make the host buffer it all.
    private static async Task<ReadOnlyMemory<byte>?> ReadBoundedAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[limit + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
            if (read == 0)
            {
                return buffer.AsMemory(0, total);
            }

            total += read;
        }

        return null;
    }
}
