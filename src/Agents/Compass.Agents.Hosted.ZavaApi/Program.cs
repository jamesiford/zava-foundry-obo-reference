using Azure.AI.AgentServer.Responses;
using Compass.Agents.Contracts;
using Compass.Agents.Contracts.Briefing;
using Compass.Agents.Hosted.ZavaApi;
using Compass.Agents.Hosted.ZavaApi.Clients;
using Compass.Shared.Observability;

// Foundry runs this container with immutable environment variables per agent version, so everything configurable is read
// from the environment (ZavaApi__McpEndpoint) rather than a settings file. The adapter serves :8088, /readiness and /responses.
ResponsesServer.Run<ZavaApiResponseHandler>(args: args, configure: builder =>
{
    builder.Services.AddTokenRedaction();

    var mcpEndpoint = new Uri(builder.Configuration["ZavaApi:McpEndpoint"] ?? "http://localhost:7071/runtime/webhooks/mcp");
    builder.Services.AddSingleton<IZavaApiMcpClient>(_ => new ZavaApiMcpClient(mcpEndpoint));

    builder.Services.AddScoped<IHostedAgent<MemberBriefingRequest, MemberBriefingResult>, ZavaApiAgent>();
});
