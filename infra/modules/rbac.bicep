// RBAC for the Compass identity chain.
//
//  1. Custom role "Compass Agent Impersonator" (a DATA action: it allows the caller to send x-ms-user-identity so
//     Foundry isolates agent sessions per end user). Never grant this - or any Foundry role - to end users.
//  2. The bot's managed identity receives, at the SAME scope:
//       - the custom role, and
//       - the built-in Foundry role that lets it invoke agents (default: "Foundry User").
//     Scope is PER AGENT (the agent application resource) when perAgentScope is true, otherwise the Foundry project.
//     Both roles are scoped identically on purpose: the built-in role's data actions are Microsoft.CognitiveServices/*,
//     which already includes impersonation, so granting it project-wide would defeat the per-agent scoping.
//  3. Key Vault Secrets User on the vault for the bot's managed identity.
//
// Role definition/assignment GUIDs are derived with guid() so re-deploys are idempotent.
// New-FoundryImpersonationRole.ps1 is the imperative fallback for the same role/assignments.

@description('Foundry account name.')
param foundryAccountName string

@description('Foundry project name.')
param foundryProjectName string

@description('Hosted agent names; must match the application names created by foundryAgents.bicep.')
param agentNames array

@description('Prompt agent names (no hosted container). The bot may invoke them but never needs x-ms-user-identity, so they get the consumer role only.')
param promptAgentNames array = []

@description('Search service name; empty skips the knowledge-base role assignments.')
param searchServiceName string = ''

@description('Principal id of the Search service managed identity (needs Cognitive Services User on the Foundry account when the KB uses a model).')
param searchServicePrincipalId string = ''

@description('Principal id of the Foundry project managed identity (the MCP connection to the knowledge base authenticates as this).')
param foundryProjectPrincipalId string = ''

@description('Assign per agent application (true) or at the Foundry project (false). Requires foundryAgents.bicep createApplications=true.')
param perAgentScope bool = true

@description('Principal (object) id of the bot user-assigned managed identity.')
param botIdentityPrincipalId string

@description('Key Vault name.')
param keyVaultName string

@description('Custom role display name. Unique per tenant, so main.bicep suffixes the environment.')
param impersonatorRoleName string

@description('Built-in role that lets the bot invoke agents. Default is "Foundry User" (53ca6127-db72-4b80-b1b0-d745d6d5456d, formerly "Azure AI User"); override if Microsoft ships a narrower agent-consumer role.')
param agentConsumerRoleDefinitionId string = '53ca6127-db72-4b80-b1b0-d745d6d5456d'

var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
var searchIndexDataReaderRoleId = '1407120a-92aa-4202-b7e9-c0e197c71c8f'
var cognitiveServicesUserRoleId = 'a97b65f3-24c7-4388-baec-2e87135dc908'
var configureKnowledgeBase = !empty(searchServiceName) && !empty(searchServicePrincipalId) && !empty(foundryProjectPrincipalId)

resource account 'Microsoft.CognitiveServices/accounts@2025-12-01' existing = {
  name: foundryAccountName

  resource project 'projects' existing = {
    name: foundryProjectName
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2025-05-01' existing = {
  name: keyVaultName
}

resource agentApplications 'Microsoft.CognitiveServices/accounts/projects/applications@2026-09-15-preview' existing = [for agentName in agentNames: {
  parent: account::project
  name: agentName
}]

resource promptAgentApplications 'Microsoft.CognitiveServices/accounts/projects/applications@2026-09-15-preview' existing = [for agentName in promptAgentNames: {
  parent: account::project
  name: agentName
}]

resource impersonatorRole 'Microsoft.Authorization/roleDefinitions@2022-05-01-preview' = {
  name: guid(account.id, 'compass-agent-impersonator')
  properties: {
    roleName: impersonatorRoleName
    description: 'Lets the Compass bot pass x-ms-user-identity to a Foundry agent endpoint so sessions are isolated per end user.'
    type: 'CustomRole'
    permissions: [
      {
        actions: []
        notActions: []
        dataActions: [
          'Microsoft.CognitiveServices/accounts/AIServices/agents/endpoints/UserIdentityImpersonation/action'
        ]
        notDataActions: []
      }
    ]
    assignableScopes: [
      account.id
    ]
  }
}

resource perAgentAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (agentName, i) in agentNames: if (perAgentScope) {
  name: guid(agentApplications[i].id, botIdentityPrincipalId, impersonatorRole.name)
  scope: agentApplications[i]
  properties: {
    principalId: botIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: impersonatorRole.id
  }
}]

resource perAgentConsumerAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (agentName, i) in agentNames: if (perAgentScope) {
  name: guid(agentApplications[i].id, botIdentityPrincipalId, agentConsumerRoleDefinitionId)
  scope: agentApplications[i]
  properties: {
    principalId: botIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', agentConsumerRoleDefinitionId)
  }
}]

// Prompt agents: invoke only. No impersonator role, because the person's identity reaches the knowledge base as their
// Search token (a structured input), not as an x-ms-user-identity assertion.
resource promptAgentConsumerAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (agentName, i) in promptAgentNames: if (perAgentScope) {
  name: guid(promptAgentApplications[i].id, botIdentityPrincipalId, agentConsumerRoleDefinitionId)
  scope: promptAgentApplications[i]
  properties: {
    principalId: botIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', agentConsumerRoleDefinitionId)
  }
}]

resource projectImpersonatorAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!perAgentScope) {
  name: guid(account::project.id, botIdentityPrincipalId, impersonatorRole.name)
  scope: account::project
  properties: {
    principalId: botIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: impersonatorRole.id
  }
}

resource projectConsumerAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!perAgentScope) {
  name: guid(account::project.id, botIdentityPrincipalId, agentConsumerRoleDefinitionId)
  scope: account::project
  properties: {
    principalId: botIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', agentConsumerRoleDefinitionId)
  }
}

resource searchService 'Microsoft.Search/searchServices@2025-05-01' existing = if (configureKnowledgeBase) {
  name: searchServiceName
}

// The Foundry project's MCP connection reaches the knowledge base as the project identity. This is only the
// workload-to-service hop; per-person trimming still comes from the person's token in x-ms-query-source-authorization.
resource projectSearchReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (configureKnowledgeBase) {
  name: guid(searchService.id, foundryProjectPrincipalId, searchIndexDataReaderRoleId)
  scope: searchService
  properties: {
    principalId: foundryProjectPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchIndexDataReaderRoleId)
  }
}

// Only needed when the knowledge base is configured with a model (answer synthesis / query planning).
resource searchFoundryUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (configureKnowledgeBase) {
  name: guid(account.id, searchServicePrincipalId, cognitiveServicesUserRoleId)
  scope: account
  properties: {
    principalId: searchServicePrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
  }
}

resource keyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, botIdentityPrincipalId, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    principalId: botIdentityPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
  }
}

output impersonatorRoleDefinitionId string = impersonatorRole.id
output assignmentScope string = perAgentScope ? 'per-agent-application' : 'project'
