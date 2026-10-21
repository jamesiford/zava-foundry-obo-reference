// Linux App Service plan + .NET 10 web app for Compass.Bot.Host, plus the bot's user-assigned managed identity.
// That identity is the principal the bot app registration's federated identity credential trusts
// (Setup-EntraApps.ps1) and the principal that receives Foundry/Key Vault RBAC (rbac.bicep).
// No secrets: every setting below is an id, URI or name.

@description('App Service plan name.')
param planName string

@description('Web app name (globally unique).')
param webAppName string

@description('User-assigned managed identity name.')
param identityName string

@description('Azure region.')
param location string

@description('Tags applied to every resource in this module.')
param tags object

@description('Value of the azd-service-name tag; azd uses it to find the deployment target for the service.')
param azdServiceName string = 'bot'

@description('App Service plan SKU.')
param skuName string = 'B1'

@description('Linux runtime stack, e.g. DOTNETCORE|10.0.')
param linuxFxVersion string = 'DOTNETCORE|10.0'

@description('Application Insights component name (same resource group).')
param appInsightsName string

@description('Entra tenant id.')
param tenantId string

@description('Application (client) id of the bot app registration (compass-bot-<env>).')
param botAppId string

@description('Key Vault URI (no secrets are stored; surfaced for optional settings).')
param keyVaultUri string

@description('Name of the Azure Bot OAuth connection used for Teams SSO (botOAuthConnection.bicep).')
param teamsSsoConnectionName string = 'teams-sso'

@description('Extra app settings merged over the defaults (Foundry endpoint, agent names, token audiences, ...).')
param additionalAppSettings object = {}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: identityName
  location: location
  tags: tags
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource plan 'Microsoft.Web/serverfarms@2025-03-01' = {
  name: planName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: skuName
  }
  properties: {
    reserved: true
  }
}

var defaultAppSettings = {
  APPLICATIONINSIGHTS_CONNECTION_STRING: appInsights.properties.ConnectionString
  // Makes DefaultAzureCredential pick the user-assigned identity.
  AZURE_CLIENT_ID: identity.properties.clientId
  SCM_DO_BUILD_DURING_DEPLOYMENT: 'false'
  KeyVault__Uri: keyVaultUri
  // Microsoft 365 Agents SDK connection (setting names per "Configure authentication in your agent" > Federated
  // credentials): ClientId is the bot app registration, FederatedClientId the managed identity whose token is the
  // client assertion. There is no client secret setting.
  Connections__ServiceConnection__Settings__AuthType: 'FederatedCredentials'
  Connections__ServiceConnection__Settings__ClientId: botAppId
  Connections__ServiceConnection__Settings__FederatedClientId: identity.properties.clientId
  Connections__ServiceConnection__Settings__TenantId: tenantId
  Connections__ServiceConnection__Settings__Scopes__0: 'https://api.botframework.com/.default'
  ConnectionsMap__0__ServiceUrl: '*'
  ConnectionsMap__0__Connection: 'ServiceConnection'
  // Teams SSO: the handler asks the Azure Bot token store for the person's token through this OAuth connection.
  AgentApplication__UserAuthorization__DefaultHandlerName: 'teamsSso'
  AgentApplication__UserAuthorization__AutoSignIn: 'true'
  AgentApplication__UserAuthorization__Handlers__teamsSso__Settings__AzureBotOAuthConnectionName: teamsSsoConnectionName
  // The OBO confidential client (dev spec section 9 hop 2): same app registration and identity as above.
  Bot__Obo__TenantId: tenantId
  Bot__Obo__ClientId: botAppId
  Bot__Obo__ManagedIdentityClientId: identity.properties.clientId
}

resource webApp 'Microsoft.Web/sites@2025-03-01' = {
  name: webAppName
  location: location
  tags: union(tags, { 'azd-service-name': azdServiceName })
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: linuxFxVersion
      alwaysOn: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      appSettings: map(items(union(defaultAppSettings, additionalAppSettings)), setting => {
        name: setting.key
        value: setting.value
      })
    }
  }
}

output webAppName string = webApp.name
output webAppId string = webApp.id
output webAppHostName string = webApp.properties.defaultHostName
output identityId string = identity.id
output identityName string = identity.name
output identityClientId string = identity.properties.clientId
output identityPrincipalId string = identity.properties.principalId
