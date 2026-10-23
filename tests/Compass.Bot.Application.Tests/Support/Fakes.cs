using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using Azure.Core;
using Compass.Agents.Contracts;
using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Contracts.Context;
using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Contracts.Retrieval;
using Compass.Bot.Application;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Models;
using Compass.Bot.Domain;
using Compass.Bot.Orchestration;
using Compass.Shared.Identity.Tokens;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AccessToken = Compass.Bot.Domain.AccessToken;

namespace Compass.Bot.Application.Tests.Support;

/// <summary>JWT-shaped strings so tests can prove a specific token value never leaks into logs or exceptions.</summary>
internal static class FakeTokens
{
    public static string Create(string marker)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{B64("{\"alg\":\"RS256\",\"typ\":\"JWT\"}")}.{B64($"{{\"sub\":\"{marker}\"}}")}.{B64("signature-" + marker)}";
    }
}

internal static class TestPeople
{
    public static readonly     PersonContext UserA = new("11111111-1111-1111-1111-111111111111", "tenant-1", "Daniel Park");
        public static readonly PersonContext UserB = new("22222222-2222-2222-2222-222222222222", "tenant-1", "Maya Lin");
}

internal sealed class CountingPersonResolver(PersonContext person) : IPersonContextResolver
{
    private int _calls;

    public int Calls => _calls;

    public Task<PersonContext> ResolveAsync(AccessToken ssoToken, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(person);
    }
}

internal sealed class FixedMemberResolver(string memberNumber) : IMemberNumberResolver
{
    public string Resolve(PersonContext person) => memberNumber;
}

/// <summary>A person with no linked member record, as for someone not yet mapped in configuration.</summary>
internal sealed class UnlinkedMemberResolver : IMemberNumberResolver
{
    public string Resolve(PersonContext person) => throw new Compass.Bot.Application.Exceptions.MemberNotLinkedException();
}

/// <summary>Issues a distinct fake token per audience and records every exchange, including the assertion it was given.</summary>
internal sealed class RecordingBroker : IObOTokenBroker
{
    private readonly ConcurrentQueue<(DownstreamAudience Audience, AccessToken Assertion)> _calls = new();

    public Exception? FailWith { get; init; }

    public DownstreamAudience? FailFor { get; init; }

    public IReadOnlyCollection<(DownstreamAudience Audience, AccessToken Assertion)> Calls => _calls;

    public Task<AccessToken> AcquireAsync(AccessToken userAssertion, DownstreamAudience audience, CancellationToken cancellationToken)
    {
        _calls.Enqueue((audience, userAssertion));
        if (FailWith is not null && (FailFor is null || FailFor == audience))
        {
            throw FailWith;
        }

        return Task.FromResult(new AccessToken(TokenFor(audience)));
    }

    public static string TokenFor(DownstreamAudience audience) => FakeTokens.Create($"obo-{audience}");
}

internal sealed class RecordingGateway : IHostedAgentGateway
{
    private readonly ConcurrentQueue<HostedAgentCall> _calls = new();

    public Exception? FailWith { get; init; }

    public HostedAgentName? FailFor { get; init; }

    /// <summary>Awaited before each call answers; lets a test hold a branch open (slow or hanging agent).</summary>
    public Func<HostedAgentCall, CancellationToken, Task>? Before { get; init; }

    /// <summary>The hits the retrieval agent returns; the default is one hit for Daniel.</summary>
    public IReadOnlyList<RetrievalHit>? RetrievalHits { get; init; }

    public IReadOnlyCollection<HostedAgentCall> Calls => _calls;

    public async Task<TResponse> InvokeAsync<TRequest, TResponse>(HostedAgentCall call, TRequest request, CancellationToken cancellationToken)
    {
        _calls.Enqueue(call);
        if (Before is not null)
        {
            await Before(call, cancellationToken);
        }

        if (FailWith is not null && (FailFor is null || FailFor == call.Agent))
        {
            throw FailWith;
        }

        object result = call.Agent switch
        {
            HostedAgentName.ZavaApi => new MemberBriefingResult(
                "ZCU-100234",
                "Daniel Park",
                "Active",
                [new AccountBriefing("1001", "Checking", "Everyday", 1250.50m, "USD")],
                1250.50m),
            HostedAgentName.SharePointKnowledge => new KnowledgeQueryResult("Daniel's knowledge answer", ["Handbook.docx"]),
            _ => new RetrievalResult(RetrievalHits ?? [new RetrievalHit("https://contoso.sharepoint.com/sites/usera", "Loan note", "Daniel's loan note", 0.9)]),
        };
        return (TResponse)result;
    }
}

/// <summary>Records every question and extract list the answer step is given, and answers or fails as scripted.</summary>
internal sealed class RecordingAnswerer : IAnswerFromExtracts
{
    private readonly ConcurrentQueue<(string Question, IReadOnlyList<RetrievalHit> Extracts)> _calls = new();

    public Func<string, IReadOnlyList<RetrievalHit>, CancellationToken, Task<KnowledgeQueryResult>> Respond { get; init; } =
        (_, _, _) => Task.FromResult(new KnowledgeQueryResult("Stub answer.", []));

    public IReadOnlyCollection<(string Question, IReadOnlyList<RetrievalHit> Extracts)> Calls => _calls;

    public Task<KnowledgeQueryResult> AnswerAsync(string question, IReadOnlyList<RetrievalHit> extracts, CancellationToken cancellationToken)
    {
        _calls.Enqueue((question, extracts));
        return Respond(question, extracts, cancellationToken);
    }
}

/// <summary>Stands in for the in-process knowledge agent when a test only exercises the HTTP-routed agents.</summary>
internal sealed class StubKnowledgeAgent : IHostedAgent<KnowledgeQueryRequest, KnowledgeQueryResult>
{
    public Func<KnowledgeQueryRequest, AgentCallContext, KnowledgeQueryResult> Respond { get; init; } =
        (_, _) => new KnowledgeQueryResult("stub answer", []);

    public List<AgentCallContext> Contexts { get; } = [];

    public Task<KnowledgeQueryResult> HandleAsync(KnowledgeQueryRequest request, AgentCallContext context, CancellationToken ct)
    {
        Contexts.Add(context);
        return Task.FromResult(Respond(request, context));
    }
}

internal sealed class StaticValidator(AccessTokenValidationResult result) : IAccessTokenValidator
{
    public Task<AccessTokenValidationResult> ValidateAsync(string? token, CancellationToken cancellationToken = default) => Task.FromResult(result);

    public static StaticValidator ValidFor(params Claim[] claims) =>
        new(new AccessTokenValidationResult(TokenValidationStatus.Valid, new ClaimsPrincipal(new ClaimsIdentity(claims, "test"))));
}

internal sealed class FakeCredential(string token) : TokenCredential
{
    public TokenRequestContext? LastRequest { get; private set; }

    public override Azure.Core.AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => throw new NotSupportedException();

    public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        LastRequest = requestContext;
        return ValueTask.FromResult(new Azure.Core.AccessToken(token, DateTimeOffset.UtcNow.AddHours(1)));
    }
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(_lines);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));
    }
}

/// <summary>Builds the real Application wiring with only the outward-facing ports replaced by fakes.</summary>
internal sealed class BotTestHost
{
    public BotTestHost(
        CountingPersonResolver? person = null,
        RecordingBroker? broker = null,
        RecordingGateway? gateway = null,
        string memberNumber = "ZCU-100234",
        IHostedAgentGateway? gatewayOverride = null,
        SharePointRoute route = SharePointRoute.Both,
        IMemberNumberResolver? members = null,
        RetrievalSettings? retrieval = null,
        IAnswerFromExtracts? answerer = null)
    {
        Person = person ?? new CountingPersonResolver(TestPeople.UserA);
        Broker = broker ?? new RecordingBroker();
        Gateway = gateway ?? new RecordingGateway();
        Logs = new CapturingLoggerProvider();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(Logs));
        services.AddCompassBotApplication(route, retrieval);
        services.AddCompassBotOrchestration();
        if (answerer is not null)
        {
            services.AddSingleton(answerer);
        }
        services.AddSingleton<IPersonContextResolver>(Person);
        services.AddSingleton(members ?? new FixedMemberResolver(memberNumber));
        services.AddSingleton<IObOTokenBroker>(Broker);
        services.AddSingleton(gatewayOverride ?? Gateway);
        Provider = services.BuildServiceProvider(validateScopes: true);
    }

    public CountingPersonResolver Person { get; }

    public RecordingBroker Broker { get; }

    public RecordingGateway Gateway { get; }

    public CapturingLoggerProvider Logs { get; }

    public IServiceProvider Provider { get; }
}
