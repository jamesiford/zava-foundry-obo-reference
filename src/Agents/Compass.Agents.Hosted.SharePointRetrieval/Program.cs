using Azure.AI.AgentServer.Responses;
using Compass.Agents.Contracts;
using Compass.Agents.Contracts.Retrieval;
using Compass.Agents.Hosted.SharePointRetrieval;
using Compass.Agents.Hosted.SharePointRetrieval.Clients;
using Compass.Agents.Hosting;
using Compass.Shared.Observability;

// Foundry runs this container with immutable environment variables per agent version, so everything configurable is read
// from the environment (Graph__BaseUrl) rather than a settings file. The adapter serves :8088, /readiness and /responses.
ResponsesServer.Run<SharePointRetrievalResponseHandler>(args: args, configure: builder =>
{
    builder.Services.AddTokenRedaction();
    builder.Services.ValidateAgent365Tracing(builder.Configuration);

    var graphBaseUrl = builder.Configuration["Graph:BaseUrl"] ?? "https://graph.microsoft.com/";
    builder.Services.AddHttpClient<IGraphRetrievalClient, GraphRetrievalClient>(client => client.BaseAddress = new Uri(graphBaseUrl));

    builder.Services.AddScoped<IHostedAgent<RetrievalRequest, RetrievalResult>, SharePointRetrievalAgent>();
});
