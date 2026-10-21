using Azure.Core;
using Compass.Bot.Orchestration;
using Azure.Identity;
using Compass.Agents.Contracts;
using Compass.Agents.Contracts.Knowledge;
using Compass.Agents.Prompt.SharePointKnowledge;
using Compass.Agents.Prompt.SharePointKnowledge.Foundry;
using Compass.Bot.Application;
using Compass.Bot.Application.Abstractions;
using Compass.Bot.Application.Queries;
using Compass.Bot.Domain;
using Compass.Bot.Infrastructure.Foundry;
using Compass.Shared.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using AccessToken = Compass.Bot.Domain.AccessToken;

namespace Compass.IdentityIsolation.Tests.Support;

/// <summary>
/// Drives the deployed hosted agents in Foundry as two real test users. Each user's tokens come from a username/password
/// sign-in against the test tenant (ROPC — test tenant only, because it cannot satisfy MFA), then go through the real
/// gateway and real agents, so SharePoint and the Zava API make the access decision for real.
/// </summary>
/// <remarks>
/// SCOPE: this skips the bot's own MSAL on-behalf-of exchange, because that needs the bot's managed identity and so can
/// only run inside Azure. The exchange is verified separately in the live handoff (infra/README.md). What this does
/// prove live is the part CI cannot: that the real stores scope results to the token's owner.
/// Not run in CI without a tenant; the code path is unexercised until the handoff steps are done.
/// </remarks>
internal sealed class LiveBackend : IIsolationBackend, IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly string _tenantId = Required("COMPASS_TEST_TENANT_ID");
    private readonly string _clientId = Required("COMPASS_TEST_CLIENT_ID");
    private readonly string[] _graphScopes = (Environment.GetEnvironmentVariable("COMPASS_TEST_GRAPH_SCOPES") ?? "https://graph.microsoft.com/.default").Split(' ', StringSplitOptions.RemoveEmptyEntries);
    private readonly string[] _searchScopes = (Environment.GetEnvironmentVariable("COMPASS_TEST_SEARCH_SCOPES") ?? "https://search.azure.com/.default").Split(' ', StringSplitOptions.RemoveEmptyEntries);
    private readonly string[] _zavaApiScopes = Required("COMPASS_TEST_ZAVAAPI_SCOPES").Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public LiveBackend()
    {
        UserA = Person("A", TestPerson.CanaryA);
        UserB = Person("B", TestPerson.CanaryB);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCompassBotApplication();
        services.AddCompassBotOrchestration();
        services.AddSingleton<IPersonContextResolver, NotUsedPersonResolver>();
        services.AddSingleton<IMemberNumberResolver, NotUsedMemberResolver>();
        services.AddSingleton<IObOTokenBroker, NotUsedBroker>();
        services.AddSingleton<TokenCredential>(new DefaultAzureCredential());
        services.Configure<FoundryProjectOptions>(o => o.ProjectEndpoint = new Uri(Required("COMPASS_TEST_FOUNDRY_PROJECT_ENDPOINT")));
        services.AddOptions<KnowledgeAgentOptions>();
        services.AddSharePointKnowledgeAgent();
        services.Configure<FoundryOptions>(o =>
        {
            o.SharePointRetrievalAgent = new HostedAgentReference { Name = Environment.GetEnvironmentVariable("COMPASS_TEST_SHAREPOINT_AGENT_NAME") ?? "SharePointRetrievalAgent" };
            o.ZavaApiAgent = new HostedAgentReference { Name = Environment.GetEnvironmentVariable("COMPASS_TEST_ZAVAAPI_AGENT_NAME") ?? "ZavaApiAgent" };
        });
        services.AddSingleton<IHostedAgentGateway, FoundryHostedAgentGateway>();        _provider = services.BuildServiceProvider(validateScopes: true);
    }

    public TestPerson UserA { get; }

    public TestPerson UserB { get; }

    public async Task<AskResult> AskAsync(TestPerson person, string question)
    {
        var prefix = $"COMPASS_TEST_USER{person.Key}";
        var upn = Required($"{prefix}_UPN");
        var password = Required($"{prefix}_PASSWORD");

        var graphToken = new AccessToken(await SignInAsync(upn, password, _graphScopes));
        var zavaToken = new AccessToken(await SignInAsync(upn, password, _zavaApiScopes));
        var searchToken = new AccessToken(await SignInAsync(upn, password, _searchScopes));

        var context = new PersonContext(person.ObjectId, person.TenantId, person.DisplayName);
        var session = SessionId.Mint(context);

        await using var scope = _provider.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var documents = sender.SendAsync(new InvokeSharePointRetrievalAgentQuery(context, session, graphToken, question), CancellationToken.None);
        var accounts = sender.SendAsync(new InvokeZavaApiAgentQuery(context, session, zavaToken, person.MemberNumber), CancellationToken.None);
        var knowledge = sender.SendAsync(new InvokeSharePointKnowledgeAgentQuery(context, session, searchToken, question), CancellationToken.None);
        return new AskResult(await documents, await accounts, await knowledge);
    }

    public void Dispose() => _provider.Dispose();

    private async Task<string> SignInAsync(string upn, string password, string[] scopes)
    {
        var app = PublicClientApplicationBuilder.Create(_clientId)
            .WithAuthority($"https://login.microsoftonline.com/{_tenantId}")
            .Build();
        // ROPC is deprecated and cannot satisfy MFA; it is acceptable only for these test-tenant users (spec §16 risk).
#pragma warning disable CS0618
        var result = await app.AcquireTokenByUsernamePassword(scopes, upn, password).ExecuteAsync();
#pragma warning restore CS0618
        return result.AccessToken;
    }

    private static TestPerson Person(string key, string canary)
    {
        var prefix = $"COMPASS_TEST_USER{key}";
        return new TestPerson(
            key,
            Required($"{prefix}_OID"),
            Required("COMPASS_TEST_TENANT_ID"),
            Required($"{prefix}_NAME"),
            Required($"{prefix}_MEMBER"),
            canary);
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Live isolation tests need the environment variable {name}.");

    // The live path calls the agent queries directly, so the Teams-turn collaborators are deliberately inert.
    private sealed class NotUsedPersonResolver : IPersonContextResolver
    {
        public Task<PersonContext> ResolveAsync(AccessToken ssoToken, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NotUsedMemberResolver : IMemberNumberResolver
    {
        public string Resolve(PersonContext person) => throw new NotSupportedException();
    }

    private sealed class NotUsedBroker : IObOTokenBroker
    {
        public Task<AccessToken> AcquireAsync(AccessToken userAssertion, DownstreamAudience audience, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
