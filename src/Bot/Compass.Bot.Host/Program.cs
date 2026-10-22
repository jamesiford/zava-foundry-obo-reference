using Azure.Monitor.OpenTelemetry.Exporter;
using Compass.Agents.Prompt.SharePointKnowledge;
using Compass.Bot.Application;
using Compass.Bot.Host;
using Compass.Bot.Host.FrontDoor;
using Compass.Bot.Host.Readiness;
using Compass.Bot.Host.Turns;
using Compass.Bot.Infrastructure;
using Compass.Bot.Orchestration;
using Compass.Shared.Observability;
using Microsoft.Agents.Hosting.AspNetCore;
using Microsoft.Agents.Storage;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Redaction first: processors run in registration order, so it must precede the exporter AddServiceDefaults adds.
builder.Services.AddTokenRedaction();
builder.AddServiceDefaults();

// Application Insights is opt-in by connection string (set by infra/modules/appService.bicep); locally the Aspire
// dashboard receives telemetry over OTLP instead. Registered after redaction so only scrubbed data is exported.
if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitorExporter();
}

// Name the service so traces and logs show "compass-bot" instead of unknown_service:dotnet, and listen to the Agent Framework's
// workflow spans (workflow.session, executor.process {id}, edge_group.process). Message content is recorded only when
// sensitive data is enabled, and it is not.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: "compass-bot"))
    .WithTracing(tracing => tracing.AddSource("Microsoft.Agents.AI.Workflows"));

builder.Services.AddCompassBotApplication(SharePointRouteSetting.Read(builder.Configuration), RetrievalSettingsReader.Read(builder.Configuration));
builder.Services.AddCompassBotOrchestration();
builder.Services.AddCompassBotInfrastructure(builder.Configuration);
builder.Services.AddScoped<BriefingTurnHandler>();
builder.Services.AddScoped<TeamsTurnResponder>();
builder.Services.AddSingleton(new StreamingOptions());
builder.Services.AddSingleton<IReplyStreamFactory, SdkReplyStreamFactory>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddFrontDoor(builder.Configuration);

builder.AddAgentDefaults();
builder.AddAgentApplicationOptions();
builder.AddAgent<CompassAgent>();
builder.Services.AddSingleton<IStorage, MemoryStorage>();
builder.AddAgentAuthorization(b => b.AddBotChannelAuthentication());

var app = builder.Build();

// One-off operator commands (infra/README.md): publish a prompt agent as a new Foundry agent version using the operator's
// own identity (DefaultAzureCredential), then exit without starting the bot.
if (args.Contains("--publish-knowledge-agent"))
{
    var published = await app.Services.GetRequiredService<SharePointKnowledgeAgentProvisioner>().PublishAsync(CancellationToken.None);
    Console.WriteLine($"Published {published.Name} version {published.Version}.");
    return;
}

if (args.Contains("--publish-answer-agent"))
{
    var published = await app.Services.GetRequiredService<SharePointAnswerAgentProvisioner>().PublishAsync(CancellationToken.None);
    Console.WriteLine($"Published {published.Name} version {published.Version}.");
    return;
}

app.UseFrontDoorPage();
app.UseAgents(useRouting: true);
app.MapFrontDoor();
app.MapDefaultAgentEndpoints();
app.MapDefaultEndpoints();
app.MapBotReadiness();

app.Run();
