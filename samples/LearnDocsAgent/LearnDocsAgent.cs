#:package Azure.AI.Projects@2.0.1
#:package Azure.AI.Projects.Agents@2.0.0
#:package Azure.AI.Extensions.OpenAI@2.0.0
#:package Azure.Identity@1.*
#:property NoWarn=OPENAI001

// Superseded by src/Agents/Compass.Agents.Prompt.LearnDocs, which does the same thing as a proper project.

// The smallest useful Foundry agent: a prompt agent whose only tool is a public, remote MCP server (Microsoft Learn).
// No code to host, no project connection, no secrets: the agent is a model, instructions and one tool.
//
//   dotnet run samples/LearnDocsAgent/LearnDocsAgent.cs                      publish the agent as a new version
//   dotnet run samples/LearnDocsAgent/LearnDocsAgent.cs -- "your question"   publish, then ask it
//
// FOUNDRY_PROJECT_ENDPOINT = https://{account}.services.ai.azure.com/api/projects/{project}
// FOUNDRY_MODEL_NAME       = the model deployment name (default gpt-5.6-sol)

using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using OpenAI.Responses;

const string AgentName = "LearnDocsAgent";

var endpoint = new Uri(Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("Set FOUNDRY_PROJECT_ENDPOINT."));
var model = Environment.GetEnvironmentVariable("FOUNDRY_MODEL_NAME") ?? "gpt-5.6-sol";
var credential = new DefaultAzureCredential();

// The tool: a remote MCP server. Foundry calls it for the agent. No approval prompt, because it is read-only documentation search.
McpTool learn = ResponseTool.CreateMcpTool(
    serverLabel: "microsoft-learn",
    serverUri: new Uri("https://learn.microsoft.com/api/mcp"),
    toolCallApprovalPolicy: new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.NeverRequireApproval));

var definition = new DeclarativeAgentDefinition(model)
{
    Instructions = "Answer questions about Microsoft Foundry, Microsoft Agent Framework, MCP and Azure using only the Microsoft Learn tool. " +
                   "Cite the Learn page URLs you used. If the tool returns nothing relevant, say you could not find it.",
    Tools = { learn },
};

var project = new AIProjectClient(endpoint, credential);
var version = (await project.AgentAdministrationClient.CreateAgentVersionAsync(
    AgentName,
    new ProjectsAgentVersionCreationOptions(definition) { Description = "Prompt agent with the Microsoft Learn remote MCP server as its only tool." })).Value;
Console.WriteLine($"Published {version.Name} version {version.Version}.");

if (args.Length > 0)
{
    var request = new CreateResponseOptions { StoredOutputEnabled = false };
    request.InputItems.Add(ResponseItem.CreateUserMessageItem(string.Join(' ', args)));
    var responses = new ProjectResponsesClient(endpoint, credential, new AgentReference(AgentName, version.Version));
    var result = (await responses.CreateResponseAsync(request)).Value;
    Console.WriteLine(result.GetOutputText());
}
