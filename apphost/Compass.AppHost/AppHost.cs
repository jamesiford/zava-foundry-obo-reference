var builder = DistributedApplication.CreateBuilder(args);

// The Foundry agents are provisioned by Bicep/azd, not orchestrated here (dev spec D6). Preview Aspire.Hosting.Foundry
// packages now exist (AsHostedAgent, AddPromptAgent); this build has not tried them. The bot reaches all three through one Foundry project endpoint; the default is an inert
// placeholder so the AppHost starts without any Azure access. Override with Parameters:<name> (user secrets or environment).
var foundryProjectEndpoint = builder.AddParameter(
    "foundry-project-endpoint",
    "https://placeholder.invalid/api/projects/compass");

// The knowledge agent's Foundry IQ knowledge base is an Azure AI Search MCP endpoint (inert until a real one is set).
var knowledgeBaseMcpEndpoint = builder.AddParameter(
    "knowledge-base-mcp-endpoint",
    "https://placeholder.invalid/knowledgebases/zava-kb/mcp?api-version=2026-08-01-preview");

// Functions need a storage account for host state; Azurite stands in for it locally.
var storage = builder.AddAzureStorage("storage").RunAsEmulator();

builder.AddAzureFunctionsProject<Projects.Compass_Mcp_ZavaApi_Host>("mcp-zava-api")
    .WithHostStorage(storage);

builder.AddProject<Projects.Compass_Bot_Host>("bot")
    .WithEnvironment("Bot__Foundry__ProjectEndpoint", foundryProjectEndpoint)
    .WithEnvironment("Bot__Foundry__KnowledgeAgent__KnowledgeBaseMcpEndpoint", knowledgeBaseMcpEndpoint)
    .WithExternalHttpEndpoints();

builder.Build().Run();
