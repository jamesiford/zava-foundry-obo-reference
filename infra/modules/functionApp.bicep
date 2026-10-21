// Flex Consumption Function App (Linux, .NET isolated) for Compass.Mcp.ZavaApi.Host, with an identity-based
// storage account and its own user-assigned managed identity.
//
// Storage: shared-key access is DISABLED. Host storage (AzureWebJobsStorage__*) and the Flex deployment
// container both authenticate with the managed identity, so the identity gets the data-plane roles below.
// Those assignments live here (not rbac.bicep) because the app cannot start without them.
//
// MCP extension: the remote MCP endpoint (/runtime/webhooks/mcp) would by default demand the `mcp_extension` system
// key as `x-functions-key`. This app turns that off (host.json extensions.mcp.system.webhookAuthorizationLevel =
// Anonymous, mirrored below as an app setting so it survives a host.json mix-up) because the Foundry agent's MCP
// connection forwards the person's token, and the app's own middleware validates its audience and issuer (dev spec
// section 9 hop 6) - a shared key identifies no one. Anonymous at the Functions layer is NOT anonymous overall: a
// request without a valid Entra JWT for the Zava API audience is rejected by that middleware. Hardening options for
// production (APIM in front, private endpoint, keeping the key as a second factor) are in infra/README.md.

@description('Function app name (globally unique).')
param functionAppName string

@description('Flex Consumption plan name.')
param planName string

@description('Host/deployment storage account name (3-24 lowercase alphanumerics, globally unique).')
@minLength(3)
@maxLength(24)
param storageAccountName string

@description('User-assigned managed identity name for the function app.')
param identityName string

@description('Azure region.')
param location string

@description('Tags applied to every resource in this module.')
param tags object

@description('Value of the azd-service-name tag; azd uses it to find the deployment target for the service.')
param azdServiceName string = 'mcp'

@description('Application Insights component name (same resource group).')
param appInsightsName string

@description('Authority used to fetch signing keys, e.g. https://login.microsoftonline.com/<tenant>/v2.0.')
param zavaApiAuthority string

@description('Accepted token audiences (ZavaApi__Auth__ValidAudiences__n).')
param zavaApiValidAudiences array

@description('Accepted token issuers (ZavaApi__Auth__ValidIssuers__n).')
param zavaApiValidIssuers array

@description('.NET isolated runtime version on Flex Consumption.')
param dotnetVersion string = '10.0'

@description('Memory per instance in MB.')
@allowed([
  512
  2048
  4096
])
param instanceMemoryMB int = 2048

@description('Maximum scale-out instance count.')
@minValue(1)
@maxValue(1000)
param maximumInstanceCount int = 40

var deploymentContainerName = 'app-package'

var storageBlobDataOwnerRoleId = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var storageQueueDataContributorRoleId = '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
var storageTableDataContributorRoleId = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'
var storageRoleIds = [
  storageBlobDataOwnerRoleId
  storageQueueDataContributorRoleId
  storageTableDataContributorRoleId
]

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: identityName
  location: location
  tags: tags
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource storage 'Microsoft.Storage/storageAccounts@2025-01-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    allowSharedKeyAccess: false
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    defaultToOAuthAuthentication: true
  }

  resource blobService 'blobServices' = {
    name: 'default'

    resource deploymentContainer 'containers' = {
      name: deploymentContainerName
      properties: {
        publicAccess: 'None'
      }
    }
  }
}

resource storageRoleAssignments 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for roleId in storageRoleIds: {
  name: guid(storage.id, identity.id, roleId)
  scope: storage
  properties: {
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleId)
  }
}]

resource plan 'Microsoft.Web/serverfarms@2025-03-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'functionapp'
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  properties: {
    reserved: true
  }
}

var authSettings = union(
  { ZavaApi__Auth__Authority: zavaApiAuthority },
  reduce(
    map(range(0, length(zavaApiValidAudiences)), i => { 'ZavaApi__Auth__ValidAudiences__${i}': zavaApiValidAudiences[i] }),
    {},
    (acc, next) => union(acc, next)
  ),
  reduce(
    map(range(0, length(zavaApiValidIssuers)), i => { 'ZavaApi__Auth__ValidIssuers__${i}': zavaApiValidIssuers[i] }),
    {},
    (acc, next) => union(acc, next)
  )
)

// FUNCTIONS_WORKER_RUNTIME is intentionally absent: Flex Consumption takes the runtime from functionAppConfig.
var appSettings = union(authSettings, {
  APPLICATIONINSIGHTS_CONNECTION_STRING: appInsights.properties.ConnectionString
  AZURE_CLIENT_ID: identity.properties.clientId
  AzureWebJobsStorage__accountName: storage.name
  AzureWebJobsStorage__credential: 'managedidentity'
  AzureWebJobsStorage__clientId: identity.properties.clientId
  AzureFunctionsJobHost__extensions__mcp__system__webhookAuthorizationLevel: 'Anonymous'
})

resource functionApp 'Microsoft.Web/sites@2025-03-01' = {
  name: functionAppName
  location: location
  tags: union(tags, { 'azd-service-name': azdServiceName })
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainerName}'
          authentication: {
            type: 'UserAssignedIdentity'
            userAssignedIdentityResourceId: identity.id
          }
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: maximumInstanceCount
        instanceMemoryMB: instanceMemoryMB
      }
      runtime: {
        name: 'dotnet-isolated'
        version: dotnetVersion
      }
    }
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: map(items(appSettings), setting => {
        name: setting.key
        value: setting.value
      })
    }
  }
  dependsOn: [
    storageRoleAssignments
    storage::blobService::deploymentContainer
  ]
}

output functionAppName string = functionApp.name
output functionAppId string = functionApp.id
output functionAppHostName string = functionApp.properties.defaultHostName
output identityId string = identity.id
output identityPrincipalId string = identity.properties.principalId
output storageAccountName string = storage.name
