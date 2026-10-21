# Compass.Agents.Prompt.LearnDocs

A teaching baseline: a Foundry **prompt agent** whose only tool is the public **Microsoft Learn MCP server**
(`https://learn.microsoft.com/api/mcp`). The whole definition is `LearnDocsAgentDefinition.cs`. It is not part of
Compass's runtime and nothing references it.

```powershell
$env:FOUNDRY_PROJECT_ENDPOINT = 'https://{account}.services.ai.azure.com/api/projects/{project}'
$env:AZURE_TOKEN_CREDENTIALS = 'AzureCliCredential'

dotnet run --project src/Agents/Compass.Agents.Prompt.LearnDocs -- publish
dotnet run --project src/Agents/Compass.Agents.Prompt.LearnDocs -- ask "How do I connect an MCP server to a Foundry prompt agent?"
```

`DefaultAzureCredential` may pick the wrong account, and the create call then returns 403. Set
`AZURE_TOKEN_CREDENTIALS=AzureCliCredential` to use the signed-in Azure CLI account.

Replaces `samples/LearnDocsAgent`.
