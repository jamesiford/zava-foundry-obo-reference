using System.ClientModel.Primitives;
using Compass.Bot.Orchestration;
using System.Collections.Concurrent;
using Azure.Core;
using Compass.Agents.Contracts;
using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Contracts.Retrieval;
using Compass.Agents.Hosted.SharePointRetrieval;
using Compass.Agents.Hosted.SharePointRetrieval.Clients;
using Compass.Agents.Hosted.ZavaApi;
using Compass.Agents.Hosted.ZavaApi.Clients;
using Compass.Agents.Hosting;
using Compass.Agents.Prompt.SharePointKnowledge.Foundry;
using Compass.Bot.Application;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Exceptions;
using Compass.Bot.Application.Models;
using Compass.Bot.Application.Queries;
using Compass.Bot.Domain;
using Compass.Bot.Host.Turns;
using Compass.Bot.Infrastructure.Foundry;
using Compass.Bot.Infrastructure.Identity;
using Compass.Bot.Infrastructure.Members;
using Compass.Shared.Identity.Tokens;
using Compass.Shared.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AccessToken = Compass.Bot.Domain.AccessToken;

namespace Compass.IdentityIsolation.Tests.Support;

/// <summary>What an agent host saw for one request, taken at the HTTP boundary.</summary>
internal sealed record AgentObservation(string Agent, string? PersonHeader, int StatusCode);

/// <summary>
/// Runs the real bot application layer, the real Foundry gateway and the two real hosted agents (behind the real
/// Foundry Responses hosting adapter) in one process. Only the outermost edges are fakes: Entra (token issuing and
/// validation), SharePoint and the Zava API. Everything that decides or forwards identity is production code.
/// </summary>
internal sealed class FakeBackend : IIsolationBackend, IAsyncDisposable
{
    private const string SharePointAgent = "SharePointRetrievalAgent";
    private const string ZavaApiAgent = "ZavaApiAgent";
    private const string ResponsesRoute = "/responses";

    private readonly WebApplication _sharePointApp;
    private readonly WebApplication _zavaApiApp;
    private readonly ServiceProvider _bot;
    private readonly ConcurrentQueue<AgentObservation> _observations;
    private readonly ConcurrentQueue<string> _responseBodies = new();
    private readonly SharePointRoute _route;

    private FakeBackend(
        WebApplication sharePointApp,
        WebApplication zavaApiApp,
        TokenDirectory tokens,
        DirectoryBackedGraphClient graph,
        DirectoryBackedZavaApiClient zavaApi,
        DirectoryBroker broker,
        CapturingLoggerProvider logs,
        ConcurrentQueue<AgentObservation> observations,
        SharePointRoute route)
    {
        _sharePointApp = sharePointApp;
        _zavaApiApp = zavaApiApp;
        Tokens = tokens;
        Graph = graph;
        ZavaApi = zavaApi;
        Knowledge = new DirectoryBackedKnowledgeAgent(tokens);
        Broker = broker;
        Logs = logs;
        _observations = observations;
        _route = route;
        _bot = BuildBot(this, route);
    }

    public TestPerson UserA => TestPerson.UserA;

    public TestPerson UserB => TestPerson.UserB;

    public TokenDirectory Tokens { get; }

    public DirectoryBackedGraphClient Graph { get; }

    public DirectoryBackedZavaApiClient ZavaApi { get; }

    public DirectoryBackedKnowledgeAgent Knowledge { get; }

    public DirectoryBroker Broker { get; }

    public CapturingLoggerProvider Logs { get; }

    public HttpClient SharePointAgentClient => _sharePointApp.GetTestClient();

    public HttpClient ZavaApiAgentClient => _zavaApiApp.GetTestClient();

    public IReadOnlyCollection<AgentObservation> Observations => _observations;

    /// <summary>Every text the system emitted: logs (with exceptions) plus every HTTP response body between bot and agents.</summary>
    public IEnumerable<string> CapturedText => Logs.Lines.Concat(_responseBodies);

    public IHostedAgentGateway Gateway => _bot.GetRequiredService<IHostedAgentGateway>();

    /// <param name="route">The SharePoint route the bot under test is composed with; <see cref="SharePointRoute.Both"/> keeps the full three-branch behaviour.</param>
    public static async Task<FakeBackend> StartAsync(SharePointRoute route = SharePointRoute.Both)
    {
        var tokens = new TokenDirectory();
        var logs = new CapturingLoggerProvider();
        var graph = new DirectoryBackedGraphClient(tokens);
        var zavaApi = new DirectoryBackedZavaApiClient(tokens);
        var broker = new DirectoryBroker(tokens);

        var observations = new ConcurrentQueue<AgentObservation>();
        var sharePoint = await StartAgentAsync<SharePointRetrievalResponseHandler>(
            "SharePointRetrieval",
            logs,
            s =>
            {
                s.AddSingleton<IGraphRetrievalClient>(graph);
                s.AddScoped<IHostedAgent<RetrievalRequest, RetrievalResult>, SharePointRetrievalAgent>();
            },
            (agent, person, status) => observations.Enqueue(new AgentObservation(agent, person, status)));
        var zava = await StartAgentAsync<ZavaApiResponseHandler>(
            "ZavaApi",
            logs,
            s =>
            {
                s.AddSingleton<IZavaApiMcpClient>(zavaApi);
                s.AddScoped<IHostedAgent<MemberBriefingRequest, MemberBriefingResult>, ZavaApiAgent>();
            },
            (agent, person, status) => observations.Enqueue(new AgentObservation(agent, person, status)));

        return new FakeBackend(sharePoint, zava, tokens, graph, zavaApi, broker, logs, observations, route);
    }

    public async Task<AskResult> AskAsync(TestPerson person, string question)
    {
        var briefing = await BriefAsync(person, question);
        return new AskResult(
            briefing.Documents ?? throw new InvalidOperationException("AskAsync needs the Both route; use BriefAsync for others."),
            briefing.Accounts,
            briefing.Knowledge ?? throw new InvalidOperationException("AskAsync needs the Both route; use BriefAsync for others."));
    }

    /// <summary>Runs the real orchestration for a freshly signed-in person; failures surface as the typed exceptions the bot sees.</summary>
    public async Task<Compass.Bot.Application.Models.MemberBriefing> BriefAsync(TestPerson person, string question)
    {
        await using var scope = _bot.CreateAsyncScope();
        var sso = Tokens.Mint(person, TokenDirectory.SsoAudience);
        return await scope.ServiceProvider.GetRequiredService<ISender>()
            .SendAsync(new GetMemberBriefingQuery(new AccessToken(sso), question), CancellationToken.None);
    }

    /// <summary>Runs one whole Teams turn, including the mapping from failure to what the person is shown.</summary>
    public async Task<TurnReply> TurnAsync(TestPerson person, string question)
    {
        await using var scope = _bot.CreateAsyncScope();
        var sso = Tokens.Mint(person, TokenDirectory.SsoAudience);
        return await scope.ServiceProvider.GetRequiredService<BriefingTurnHandler>().HandleAsync(sso, question, CancellationToken.None);
    }

    /// <summary>Feeds an exception into the captured output, because <c>ToString()</c> is where tokens tend to leak.</summary>
    public void Observe(Exception exception) => Logs.Add($"exception: {exception}");

    public async ValueTask DisposeAsync()
    {
        await _bot.DisposeAsync();
        await _sharePointApp.DisposeAsync();
        await _zavaApiApp.DisposeAsync();
    }

    private static async Task<WebApplication> StartAgentAsync<THandler>(
        string agentName,
        CapturingLoggerProvider logs,
        Action<IServiceCollection> register,
        Action<string, string?, int> observe)
        where THandler : Azure.AI.AgentServer.Responses.ResponseHandler
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders().SetMinimumLevel(LogLevel.Trace).AddProvider(logs);
        register(builder.Services);
        builder.Services.AddHostedAgentResponses<THandler>();

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            await next();
            if (context.Request.Path == ResponsesRoute)
            {
                observe(agentName, context.Request.Headers[CompassHeaders.UserIdentity].ToString(), context.Response.StatusCode);
            }
        });
        app.MapHostedAgentResponses();
        await app.StartAsync();
        return app;
    }

    private static ServiceProvider BuildBot(FakeBackend self, SharePointRoute route)
    {
        var services = new ServiceCollection();
        ConfigureBot(services, self, route);
        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>Starts the real web front door (the Host's <c>/api/briefing</c> endpoint) over this backend's bot services.</summary>
    public Task<WebApplication> StartFrontDoorAsync(Action<WebApplicationBuilder>? configure = null) =>
        FrontDoorHost.StartAsync(services => ConfigureBot(services, this, _route), configure);

    private static void ConfigureBot(IServiceCollection services, FakeBackend self, SharePointRoute route)
    {
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(self.Logs));
        services.AddCompassBotApplication(route);
        services.AddCompassBotOrchestration();

        services.AddSingleton<IAccessTokenValidator>(new DirectoryTokenValidator(self.Tokens));
        services.AddSingleton<IPersonContextResolver, TeamsSsoPersonContextResolver>();
        services.AddSingleton<IObOTokenBroker>(self.Broker);

        // The real resolver, fed the same mapping the demo would configure, so member numbers are never typed input.
        services.Configure<MemberDirectoryOptions>(o =>
        {
            o.ByObjectId[TestPerson.UserA.ObjectId] = TestPerson.UserA.MemberNumber;
            o.ByObjectId[TestPerson.UserB.ObjectId] = TestPerson.UserB.MemberNumber;
        });
        services.AddSingleton<IMemberNumberResolver, ConfiguredMemberNumberResolver>();

        services.Configure<FoundryOptions>(o =>
        {
            o.SharePointRetrievalAgent = new HostedAgentReference { Name = SharePointAgent };
            o.ZavaApiAgent = new HostedAgentReference { Name = ZavaApiAgent };
        });
        var routing = new RoutingHandler(
            new Dictionary<string, HttpMessageInvoker>(StringComparer.OrdinalIgnoreCase)
            {
                [SharePointAgent] = new(self._sharePointApp.GetTestServer().CreateHandler()),
                [ZavaApiAgent] = new(self._zavaApiApp.GetTestServer().CreateHandler()),
            },
            self._responseBodies);
        services.AddSingleton<IHostedAgent<KnowledgeQueryRequest, KnowledgeQueryResult>>(self.Knowledge);
        services.AddSingleton<IHostedAgentGateway>(sp => new FoundryHostedAgentGateway(
            new FoundryResponsesClientFactory(
                Options.Create(new FoundryProjectOptions { ProjectEndpoint = new Uri("https://foundry.test/api/projects/compass") }),
                new StaticCredential(FakeJwt.Create("bot-own-foundry-token")),
                new HttpClientPipelineTransport(new HttpClient(routing))),
            sp.GetRequiredService<IOptions<FoundryOptions>>(),
            sp.GetRequiredService<IHostedAgent<KnowledgeQueryRequest, KnowledgeQueryResult>>()));
        services.AddScoped<BriefingTurnHandler>();
    }

    /// <summary>
    /// Stands in for Foundry's routing: picks the hosted agent from <c>agent_reference.name</c> in the request body and
    /// replays the call, headers intact, against that agent's own <c>/responses</c> endpoint.
    /// </summary>
    private sealed class RoutingHandler(IReadOnlyDictionary<string, HttpMessageInvoker> routes, ConcurrentQueue<string> bodies) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);

            // Foundry refuses a hosted agent on the project-level route; reproduce that so the gateway can't regress to it.
            if (request.RequestUri!.AbsolutePath.EndsWith("/openai/v1/responses", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":{\"code\":\"bad_request\",\"message\":\"Hosted agents can only be called through the agent endpoint\"}}", System.Text.Encoding.UTF8, "application/json"),
                };
            }

            using var document = System.Text.Json.JsonDocument.Parse(body);
            var agent = document.RootElement.GetProperty("agent_reference").GetProperty("name").GetString()!;

            request.RequestUri = new Uri($"http://{agent}.test{ResponsesRoute}");

            // Foundry authenticates the caller and injects the asserted person as x-agent-user-id; the container never
            // treats x-ms-user-identity as an authority, so mirror that here.
            if (request.Headers.TryGetValues(CompassHeaders.UserIdentity, out var asserted))
            {
                request.Headers.Remove(CompassHeaders.AgentUserId);
                request.Headers.TryAddWithoutValidation(CompassHeaders.AgentUserId, asserted);
            }
            var response = await routes[agent].SendAsync(request, cancellationToken);
            await response.Content.LoadIntoBufferAsync(cancellationToken);
            bodies.Enqueue(await response.Content.ReadAsStringAsync(cancellationToken));
            return response;
        }
    }
    private sealed class StaticCredential(string token) : TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(token, DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}

/// <summary>Fake OBO: exchanges a live SSO token for a fresh token scoped to one audience, and remembers every exchange.</summary>
internal sealed class DirectoryBroker(TokenDirectory directory) : IObOTokenBroker
{
    private readonly ConcurrentQueue<(DownstreamAudience Audience, string Issued)> _exchanges = new();

    public IReadOnlyCollection<(DownstreamAudience Audience, string Issued)> Exchanges => _exchanges;

    public Task<AccessToken> AcquireAsync(AccessToken userAssertion, DownstreamAudience audience, CancellationToken cancellationToken)
    {
        if (!directory.TryGet(userAssertion.Reveal(), out var sso) || sso.State != TokenState.Valid)
        {
            throw new TokenAcquisitionException(audience, "The SSO assertion was not accepted.");
        }

        var issued = directory.Mint(sso.Person, audience.ToString());
        _exchanges.Enqueue((audience, issued));
        return Task.FromResult(new AccessToken(issued));
    }
}

/// <summary>Fake Entra validation for the Teams SSO token: audience, lifetime and subject all come from the directory.</summary>
internal sealed class DirectoryTokenValidator(TokenDirectory directory) : IAccessTokenValidator
{
    public Task<AccessTokenValidationResult> ValidateAsync(string? token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult(new AccessTokenValidationResult(TokenValidationStatus.Missing));
        }

        if (!directory.TryGet(token, out var grant))
        {
            return Task.FromResult(new AccessTokenValidationResult(TokenValidationStatus.InvalidSignature));
        }

        var status = grant.Audience != TokenDirectory.SsoAudience ? TokenValidationStatus.InvalidAudience
            : grant.State == TokenState.WrongIssuer ? TokenValidationStatus.InvalidIssuer
            : grant.State == TokenState.Expired ? TokenValidationStatus.Expired
            : TokenValidationStatus.Valid;
        return Task.FromResult(new AccessTokenValidationResult(status, status == TokenValidationStatus.Valid ? directory.ToPrincipal(grant) : null));
    }
}
