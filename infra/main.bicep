// Compass OBO reference build - infrastructure entry point (resource group scope; driven by `azd provision`).
//
// This file owns every shared value (naming, tags, location, identity ids). Modules receive them as parameters
// and never re-derive them (DRY, spec section 6).
//
// Module order (declared dependencies, not just file order):
//   appInsights -> keyVault -> functionApp -> appService -> botService -> botOAuthConnection (Teams SSO)
//   aiSearch (knowledge base host; the KB itself is created by scripts/Setup-KnowledgeBase.ps1)
//   foundryProject -> foundryAgents -> rbac (needs appService's managed identity + keyVault + aiSearch)

targetScope = 'resourceGroup'

@description('Deployment environment.')
@allowed([
  'dev'
  'test'
])
param environmentName string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('Short workload name used as the base of every resource name.')
@minLength(3)
@maxLength(12)
param workloadName string = 'compass'

@description('Extra tags merged over the default workload/environment tags.')
param tags object = {}

@description('Entra tenant id.')
param tenantId string = tenant().tenantId

@description('Application (client) id of the bot app registration. Produced by Setup-EntraApps.ps1 (azd env BOT_APP_ID).')
@minLength(36)
@maxLength(36)
param botAppId string

@description('Application (client) id of the Zava Credit Union API app registration. Produced by Setup-EntraApps.ps1 (azd env ZAVA_API_APP_ID).')
@minLength(36)
@maxLength(36)
param zavaApiAppId string

@description('Extra audiences the MCP function accepts in addition to the Zava API identifier URI and app id.')
param additionalZavaApiAudiences array = []

@description('Hosted agent names (one class / one hosted agent each).')
param agentNames array = [
  'SharePointRetrievalAgent'
  'ZavaApiAgent'
]

@description('Foundry prompt agent names (no container). Index 0 is the knowledge agent (published by `Compass.Bot.Host --publish-knowledge-agent`), index 1 the answer agent that writes the route A answer from the retrieved extracts (published by `Compass.Bot.Host --publish-answer-agent`).')
param promptAgentNames array = [
  'SharePointKnowledgeAgent'
  'SharePointAnswerAgent'
]

@description('Provision the Azure AI Search service that hosts the Foundry IQ knowledge base (the second SharePoint route).')
param deployKnowledgeBase bool = true

@description('Knowledge base name created by scripts/Setup-KnowledgeBase.ps1.')
param knowledgeBaseName string = 'zava-kb'

@description('Foundry project connection name created by scripts/Setup-KnowledgeBase.ps1.')
param knowledgeBaseConnectionName string = 'compass-knowledge-base'

@description('Which SharePoint route(s) the bot runs: KnowledgeBase (Foundry IQ, Search token), Retrieval (Graph, Graph token) or Both. Override with azd env set SHAREPOINT_ROUTE <value>; unknown values are rejected here and again by the bot at startup.')
@allowed([
  'KnowledgeBase'
  'Retrieval'
  'Both'
])
param sharePointRoute string = 'KnowledgeBase'

@description('Route A (Retrieval) minimum relevanceScore, 0 to 1, as text because Bicep has no decimal type. Retrieved extracts scoring below it are dropped before the answer is written; hits with no score are always kept. 0 keeps everything. The bot validates the value at startup and /health/ready reports a bad one by name. Override with azd env set RETRIEVAL_MIN_RELEVANCE <value>.')
param retrievalMinRelevance string = '0'

@description('Route A (Retrieval): have the SharePointAnswerAgent write a short cited answer from the retrieved extracts instead of listing them. Override with azd env set RETRIEVAL_SUMMARISE false.')
param retrievalSummarise bool = true

@description('Serve the web front door (the sign-in page and POST /api/briefing) from the bot host. On by default; turn it off with azd env set FRONT_DOOR_ENABLED false.')
param frontDoorEnabled bool = true

@description('Create the per-agent Foundry application resources (anchor for per-agent RBAC). Default false: the current preview API (2026-09-15-preview) rejected the documented request shape with "Missing discriminator property [AuthorizationScheme]" when this was tried, so RBAC is assigned at project scope instead. Set true to retry per-agent scope when the preview service and spec agree. See foundryAgents.bicep.')
param createAgentApplications bool = false

@description('Create Hosted agentDeployments. Only after agent versions are published; see foundryAgents.bicep.')
param createAgentDeployments bool = false

@description('Map of agent name -> published agent version, used when createAgentDeployments is true.')
param agentVersions object = {}

@description('Published version of the SharePoint retrieval hosted agent (from Compass.Tools.PublishAgents; azd env SHAREPOINT_RETRIEVAL_AGENT_VERSION). Empty until published.')
param sharePointRetrievalAgentVersion string = ''

@description('Published version of the Zava API hosted agent (from Compass.Tools.PublishAgents; azd env ZAVA_API_AGENT_VERSION). Empty until published.')
param zavaApiAgentVersion string = ''

@description('Create a model deployment in the Foundry account.')
param deployFoundryModel bool = true

@description('Model deployment name referenced by the hosted agents.')
param foundryModelDeploymentName string = 'gpt-5.6-sol'

@description('Foundry model name.')
param foundryModelName string = 'gpt-5.6-sol'

@description('Foundry model version.')
param foundryModelVersion string = '2026-07-09'

@description('Built-in role that lets the bot identity invoke agents (default "Foundry User"). Verify the id for your tenant/cloud.')
param agentConsumerRoleDefinitionId string = '53ca6127-db72-4b80-b1b0-d745d6d5456d'

@description('Tag resources SecurityControl=Ignore (governance Modify policies, e.g. forcing storage publicNetworkAccess=Disabled) and CostControl=Ignore (the governance automation that resized the bot App Service plan to Free overnight on 18 Oct 2026; whether this tag stops it is unverified). Sandbox/demo tenants only: the Flex Function deployment needs the storage endpoint reachable. Leave false in any real environment and use private endpoints instead.')
param sandboxPolicyOptOut bool = false

@description('Create the Azure Bot OAuth connection that powers Teams SSO (needs the federated credential from Setup-EntraApps.ps1).')
param deployBotOAuthConnection bool = true

@description('Name of the Azure Bot OAuth connection; the bot references it as AgentApplication:UserAuthorization:Handlers:teamsSso.')
param teamsSsoConnectionName string = 'teams-sso'

@description('Unique id in the Teams SSO federated credential subject. Must equal Setup-EntraApps.ps1 output TeamsSsoUniqueId.')
param teamsSsoUniqueId string = 'compass-teams-sso-${environmentName}'

@description('Enable Key Vault purge protection (irreversible).')
param enableKeyVaultPurgeProtection bool = false

@description('App Service plan SKU for the bot.')
param botPlanSku string = 'B1'

var suffix = take(uniqueString(resourceGroup().id), 6)
var baseName = '${workloadName}-${environmentName}'
var compactName = toLower(replace('${workloadName}${environmentName}', '-', ''))
var resourceTags = union({
  workload: workloadName
  environment: environmentName
}, sandboxPolicyOptOut ? { SecurityControl: 'Ignore', CostControl: 'Ignore' } : {}, tags)

var names = {
  logAnalytics: 'log-${baseName}-${suffix}'
  appInsights: 'appi-${baseName}-${suffix}'
  keyVault: take('kv-${compactName}-${suffix}', 24)
  botIdentity: 'id-bot-${baseName}-${suffix}'
  botPlan: 'asp-bot-${baseName}-${suffix}'
  botWebApp: 'app-bot-${baseName}-${suffix}'
  azureBot: 'bot-${baseName}-${suffix}'
  mcpIdentity: 'id-mcp-${baseName}-${suffix}'
  mcpPlan: 'plan-mcp-${baseName}-${suffix}'
  mcpFunctionApp: 'func-mcp-${baseName}-${suffix}'
  mcpStorage: take('st${compactName}${suffix}', 24)
  foundryAccount: 'aif-${baseName}-${suffix}'
  foundryProject: 'proj-${baseName}'
  search: take('srch-${baseName}-${suffix}', 60)
  impersonatorRole: 'Compass Agent Impersonator (${environmentName})'
}

var entraAuthority = '${environment().authentication.loginEndpoint}${tenantId}/v2.0'

// Explicit per-agent versions win over the generic map, and blanks never erase a version that was set the other way.
var explicitAgentVersions = toObject(
  filter([
    { name: agentNames[0], version: sharePointRetrievalAgentVersion }
    { name: agentNames[1], version: zavaApiAgentVersion }
  ], entry => !empty(entry.version)),
  entry => entry.name,
  entry => entry.version)
var effectiveAgentVersions = union(agentVersions, explicitAgentVersions)
var zavaApiIdentifierUri = 'api://zava-credit-union-api-${zavaApiAppId}'
var botIdentifierUri = 'api://botid-${botAppId}'
// v2 access tokens carry the app id (GUID) as `aud`; v1 tokens carry the identifier URI. Accept both.
var zavaApiValidAudiences = union([
  zavaApiIdentifierUri
  zavaApiAppId
], additionalZavaApiAudiences)

module appInsights 'modules/appInsights.bicep' = {
  name: 'appInsights'
  params: {
    logAnalyticsName: names.logAnalytics
    appInsightsName: names.appInsights
    location: location
    tags: resourceTags
  }
}

module keyVault 'modules/keyVault.bicep' = {
  name: 'keyVault'
  params: {
    keyVaultName: names.keyVault
    location: location
    tags: resourceTags
    tenantId: tenantId
    enablePurgeProtection: enableKeyVaultPurgeProtection
  }
  dependsOn: [
    appInsights
  ]
}

module functionApp 'modules/functionApp.bicep' = {
  name: 'functionApp'
  params: {
    functionAppName: names.mcpFunctionApp
    planName: names.mcpPlan
    storageAccountName: names.mcpStorage
    identityName: names.mcpIdentity
    location: location
    tags: resourceTags
    azdServiceName: 'mcp'
    appInsightsName: appInsights.outputs.appInsightsName
    zavaApiAuthority: entraAuthority
    zavaApiValidAudiences: zavaApiValidAudiences
    zavaApiValidIssuers: [
      entraAuthority
    ]
  }
  dependsOn: [
    keyVault
  ]
}

module aiSearch 'modules/aiSearch.bicep' = if (deployKnowledgeBase) {
  name: 'aiSearch'
  params: {
    searchName: names.search
    location: location
    tags: resourceTags
  }
}

module appService 'modules/appService.bicep' = {
  name: 'appService'
  params: {
    planName: names.botPlan
    webAppName: names.botWebApp
    identityName: names.botIdentity
    location: location
    tags: resourceTags
    azdServiceName: 'bot'
    skuName: botPlanSku
    appInsightsName: appInsights.outputs.appInsightsName
    tenantId: tenantId
    botAppId: botAppId
    keyVaultUri: keyVault.outputs.keyVaultUri
    teamsSsoConnectionName: teamsSsoConnectionName
    additionalAppSettings: {
      Bot__Channel__ValidAudiences__0: botAppId
      // Teams SSO token validation (hop 3): audience is the bot's own API, never the Zava API.
      Bot__Auth__Authority: entraAuthority
      Bot__Auth__ValidIssuers__0: entraAuthority
      Bot__Auth__ValidAudiences__0: botIdentifierUri
      Bot__Auth__ValidAudiences__1: botAppId
      Bot__Obo__ZavaApiScopes__0: '${zavaApiIdentifierUri}/.default'
      Bot__Obo__SearchScopes__0: 'https://search.azure.com/.default'
      Bot__SharePoint__Route: sharePointRoute
      Bot__FrontDoor__Enabled: string(frontDoorEnabled)
      Bot__Foundry__SharePointRetrievalAgent__Name: agentNames[0]
      Bot__Foundry__SharePointRetrievalAgent__Version: effectiveAgentVersions[?agentNames[0]] ?? ''
      Bot__Foundry__ZavaApiAgent__Name: agentNames[1]
      Bot__Foundry__ZavaApiAgent__Version: effectiveAgentVersions[?agentNames[1]] ?? ''
      Bot__Foundry__ProjectEndpoint: 'https://${names.foundryAccount}.services.ai.azure.com/api/projects/${names.foundryProject}'
      Bot__Foundry__KnowledgeAgent__AgentName: promptAgentNames[0]
      Bot__Foundry__KnowledgeAgent__ModelDeployment: foundryModelDeploymentName
      Bot__Foundry__AnswerAgent__AgentName: promptAgentNames[1]
      Bot__Foundry__AnswerAgent__ModelDeployment: foundryModelDeploymentName
      Bot__SharePoint__Retrieval__MinRelevance: retrievalMinRelevance
      Bot__SharePoint__Retrieval__Summarise: string(retrievalSummarise)
      Bot__Foundry__KnowledgeAgent__ProjectConnectionName: knowledgeBaseConnectionName
      Bot__Foundry__KnowledgeAgent__KnowledgeBaseMcpEndpoint: deployKnowledgeBase ? 'https://${names.search}.search.windows.net/knowledgebases/${knowledgeBaseName}/mcp?api-version=2026-08-01-preview' : ''
    }
  }
}

module botService 'modules/botService.bicep' = {
  name: 'botService'
  params: {
    botName: names.azureBot
    tags: resourceTags
    botAppId: botAppId
    tenantId: tenantId
    messagingEndpoint: 'https://${appService.outputs.webAppHostName}/api/messages'
  }
}

module botOAuthConnection 'modules/botOAuthConnection.bicep' = if (deployBotOAuthConnection) {
  name: 'botOAuthConnection'
  params: {
    botName: botService.outputs.botName
    connectionName: teamsSsoConnectionName
    botAppId: botAppId
    tenantId: tenantId
    tokenExchangeUrl: botIdentifierUri
    uniqueIdentifier: teamsSsoUniqueId
    scopes: '${botIdentifierUri}/access_as_user'
  }
}

module foundryProject 'modules/foundryProject.bicep' = {
  name: 'foundryProject'
  params: {
    accountName: names.foundryAccount
    projectName: names.foundryProject
    location: location
    tags: resourceTags
    deployModel: deployFoundryModel
    modelDeploymentName: foundryModelDeploymentName
    modelName: foundryModelName
    modelVersion: foundryModelVersion
    appInsightsName: appInsights.outputs.appInsightsName
  }
  dependsOn: [
    appService
    functionApp
  ]
}

module foundryAgents 'modules/foundryAgents.bicep' = {
  name: 'foundryAgents'
  params: {
    accountName: foundryProject.outputs.accountName
    projectName: foundryProject.outputs.projectName
    agentNames: agentNames
    promptAgentNames: promptAgentNames
    createApplications: createAgentApplications
    createDeployments: createAgentDeployments
    agentVersions: effectiveAgentVersions
  }
}

module rbac 'modules/rbac.bicep' = {
  name: 'rbac'
  params: {
    foundryAccountName: foundryProject.outputs.accountName
    foundryProjectName: foundryProject.outputs.projectName
    agentNames: agentNames
    promptAgentNames: promptAgentNames
    searchServiceName: deployKnowledgeBase ? names.search : ''
    searchServicePrincipalId: deployKnowledgeBase ? aiSearch!.outputs.searchPrincipalId : ''
    foundryProjectPrincipalId: foundryProject.outputs.projectPrincipalId
    perAgentScope: createAgentApplications
    botIdentityPrincipalId: appService.outputs.identityPrincipalId
    keyVaultName: keyVault.outputs.keyVaultName
    impersonatorRoleName: names.impersonatorRole
    agentConsumerRoleDefinitionId: agentConsumerRoleDefinitionId
  }
  dependsOn: [
    foundryAgents
  ]
}

// azd reads these outputs into the environment (.azure/<env>/.env).
output AZURE_TENANT_ID string = tenantId
output AZURE_LOCATION string = location
output SERVICE_BOT_RESOURCE_NAME string = appService.outputs.webAppName
output SERVICE_BOT_ENDPOINT string = 'https://${appService.outputs.webAppHostName}'
output SERVICE_MCP_RESOURCE_NAME string = functionApp.outputs.functionAppName
output SERVICE_MCP_ENDPOINT string = 'https://${functionApp.outputs.functionAppHostName}'
output BOT_MANAGED_IDENTITY_CLIENT_ID string = appService.outputs.identityClientId
output BOT_MANAGED_IDENTITY_PRINCIPAL_ID string = appService.outputs.identityPrincipalId
output BOT_MANAGED_IDENTITY_NAME string = appService.outputs.identityName
output AZURE_BOT_NAME string = botService.outputs.botName
output AZURE_KEY_VAULT_NAME string = keyVault.outputs.keyVaultName
output AZURE_KEY_VAULT_ENDPOINT string = keyVault.outputs.keyVaultUri
output APPLICATIONINSIGHTS_NAME string = appInsights.outputs.appInsightsName
output FOUNDRY_ACCOUNT_NAME string = foundryProject.outputs.accountName
output FOUNDRY_ACCOUNT_ID string = foundryProject.outputs.accountId
output FOUNDRY_PROJECT_NAME string = foundryProject.outputs.projectName
output FOUNDRY_PROJECT_ENDPOINT string = foundryProject.outputs.projectEndpoint
output FOUNDRY_AGENT_NAMES array = agentNames
output FOUNDRY_PROMPT_AGENT_NAMES array = promptAgentNames
output AZURE_SEARCH_NAME string = deployKnowledgeBase ? names.search : ''
output AZURE_SEARCH_ENDPOINT string = deployKnowledgeBase ? aiSearch!.outputs.searchEndpoint : ''
output KNOWLEDGE_BASE_NAME string = knowledgeBaseName
output KNOWLEDGE_BASE_CONNECTION_NAME string = knowledgeBaseConnectionName
output BOT_OAUTH_CONNECTION_NAME string = deployBotOAuthConnection ? teamsSsoConnectionName : ''
output FOUNDRY_RBAC_SCOPE string = rbac.outputs.assignmentScope
output IMPERSONATOR_ROLE_NAME string = names.impersonatorRole


