using System.ClientModel;
using Azure.AI.Projects;
using Azure.Identity;
using Compass.Agents.Prompt.LearnDocs;
using Microsoft.Extensions.Options;

// dotnet run -- publish             publish a new agent version
// dotnet run -- ask "<question>"    ask the latest version (publishing first if the agent does not exist yet)
// FOUNDRY_PROJECT_ENDPOINT = https://{account}.services.ai.azure.com/api/projects/{project}

var endpoint = new Uri(Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("Set FOUNDRY_PROJECT_ENDPOINT."));
var credential = new DefaultAzureCredential();
var options = Options.Create(new LearnDocsAgentOptions());
var provisioner = new LearnDocsAgentProvisioner(new AIProjectClient(endpoint, credential), options);

switch (args)
{
    case ["publish"]:
        await PublishAsync();
        break;

    case ["ask", .. { Length: > 0 } question]:
        var client = new LearnDocsAgentClient(endpoint, credential, options);
        var text = string.Join(' ', question);
        try
        {
            Console.WriteLine(await client.AskAsync(text));
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            var version = await PublishAsync();
            Console.WriteLine(await client.AskAsync(text, version));
        }

        break;

    default:
        Console.Error.WriteLine("Usage: publish | ask \"<question>\"");
        return 1;
}

return 0;

async Task<string> PublishAsync()
{
    var version = await provisioner.PublishAsync(CancellationToken.None);
    Console.WriteLine($"Published {version.Name} version {version.Version}.");
    return version.Version;
}
