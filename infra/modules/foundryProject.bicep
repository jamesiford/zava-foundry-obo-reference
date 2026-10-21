// Microsoft Foundry account (kind AIServices) with project management enabled, one Foundry project, and an
// optional model deployment for the hosted agents.
//
// ARM coverage: Microsoft.CognitiveServices/accounts and accounts/projects (API 2025-12-01) are first-class
// ARM types, so this module is fully declarative.
//
// Still manual / outside this module (see infra/README.md "Live handoff"):
//   - Hosted agent *versions* (container image + definition) are created through the Foundry data plane
//     (Foundry SDK / azd ai agent tooling), not ARM. See foundryAgents.bicep.
//   - Publishing an agent to a Microsoft 365 / Teams channel is a portal/data-plane step and is not needed for this
//     build (the Compass bot calls the agent endpoints directly).
//   - A capability host / bring-your-own storage, search and Cosmos for agent state is not provisioned; the
//     platform-managed defaults are used.
//   - The Azure AI model quota for `modelName` in `location` must already exist; otherwise set deployModel=false.

@description('Foundry (AIServices) account name; also used as the custom subdomain.')
@minLength(2)
@maxLength(64)
param accountName string

@description('Foundry project name.')
param projectName string

@description('Azure region.')
param location string

@description('Tags applied to every resource in this module.')
param tags object

@description('Friendly project name shown in the Foundry portal.')
param projectDisplayName string = projectName

@description('Public network access for the Foundry account.')
@allowed([
  'Enabled'
  'Disabled'
])
param publicNetworkAccess string = 'Enabled'

@description('Create a model deployment inside the account.')
param deployModel bool = true

@description('Model deployment name that the hosted agents reference.')
param modelDeploymentName string = 'gpt-5.6-sol'

@description('Model name.')
param modelName string = 'gpt-5.6-sol'

@description('Model version.')
param modelVersion string = '2026-07-09'

@description('Model deployment SKU.')
param modelSkuName string = 'GlobalStandard'

@description('Model deployment capacity (thousands of tokens per minute for token-based SKUs).')
param modelCapacity int = 50

@description('Name of an existing Application Insights resource to connect for Foundry agent tracing. Empty skips the connection (tracing stays off).')
param appInsightsName string = ''

resource account 'Microsoft.CognitiveServices/accounts@2025-12-01' = {
  name: accountName
  location: location
  tags: tags
  kind: 'AIServices'
  sku: {
    name: 'S0'
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    customSubDomainName: accountName
    allowProjectManagement: true
    // Keyless: callers use Entra tokens (the bot's managed identity), never account keys.
    disableLocalAuth: true
    publicNetworkAccess: publicNetworkAccess
  }
}

resource project 'Microsoft.CognitiveServices/accounts/projects@2025-12-01' = {
  parent: account
  name: projectName
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    displayName: projectDisplayName
    description: 'Compass OBO reference build - hosts the SharePointRetrievalAgent and ZavaApiAgent.'
  }
}

resource modelDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-12-01' = if (deployModel) {
  parent: account
  name: modelDeploymentName
  sku: {
    name: modelSkuName
    capacity: modelCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: modelName
      version: modelVersion
    }
  }
  dependsOn: [
    project
  ]
}

// Server-side tracing: Foundry traces prompt and hosted agents once an Application Insights resource is connected to
// the project (no code change). The project's managed identity publishes the telemetry, so there is no key to store.
resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = if (!empty(appInsightsName)) {
  name: appInsightsName
}

resource tracingConnection 'Microsoft.CognitiveServices/accounts/projects/connections@2025-12-01' = if (!empty(appInsightsName)) {
  parent: project
  name: '${appInsightsName}-tracing'
  properties: {
    category: 'AppInsights'
    authType: 'ProjectManagedIdentity'
    isSharedToAll: false
    target: appInsights.id
    metadata: {
      ApiType: 'Azure'
      ResourceId: appInsights.id
      ApplicationInsightsConnectionString: appInsights.properties.ConnectionString
    }
  }
}

var monitoringMetricsPublisherRoleId = '3913510d-42f4-4e42-8a64-420c390055eb'

resource tracingPublisher 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(appInsightsName)) {
  scope: appInsights
  name: guid(appInsights.id, project.id, monitoringMetricsPublisherRoleId)
  properties: {
    principalId: project.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', monitoringMetricsPublisherRoleId)
  }
}

output accountId string = account.id
output accountName string = account.name
output projectId string = project.id
output projectName string = project.name
output projectPrincipalId string = project.identity.principalId
output projectEndpoint string = 'https://${accountName}.services.ai.azure.com/api/projects/${projectName}'
output modelDeploymentName string = deployModel ? modelDeploymentName : ''
