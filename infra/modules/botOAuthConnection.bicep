// Azure Bot OAuth connection for Teams SSO, secretless ("AAD v2 with Federated Credentials").
//
// The bot's `AgentApplication:UserAuthorization:Handlers:teamsSso` handler asks the Bot Service token store for the
// signed-in person's token by this connection name. The store authenticates to the bot app registration with a
// projected-identity federated credential (no client secret), created by Setup-EntraApps.ps1 with the same unique id.
// Reference: https://learn.microsoft.com/microsoft-365/agents-sdk/azure-bot-user-authorization-federated-credentials
//
// The provider id below is the "AAD v2 with Federated Credentials" service provider as published in Microsoft's Agents
// Toolkit sample (OfficeDev/microsoft-365-agents-toolkit-samples, bot-oauth-connection.bicep); the portal shows the same
// provider name. `az bot authsetting list-providers` can confirm it for your cloud.

@description('Azure Bot resource name the connection belongs to.')
param botName string

@description('Connection name referenced by the bot (AgentApplication:UserAuthorization:Handlers:teamsSso:Settings:AzureBotOAuthConnectionName).')
param connectionName string = 'teams-sso'

@description('Application (client) id of the bot app registration.')
param botAppId string

@description('Entra tenant id (single tenant).')
param tenantId string

@description('Token exchange URL for Teams SSO: the bot app identifier URI, api://botid-{botAppId}.')
param tokenExchangeUrl string

@description('Unique id embedded in the federated credential subject (Setup-EntraApps.ps1 output TeamsSsoUniqueId).')
param uniqueIdentifier string

@description('Scope requested for the person. Must be an exchangeable token for OBO, so it targets the bot app\'s own API.')
param scopes string

resource bot 'Microsoft.BotService/botServices@2022-09-15' existing = {
  name: botName
}

resource connection 'Microsoft.BotService/botServices/connections@2022-09-15' = {
  parent: bot
  name: connectionName
  location: 'global'
  properties: {
    serviceProviderId: 'c00b44ab-5e16-c44c-af26-2fd5bc55eb18'
    serviceProviderDisplayName: 'AAD v2 with Federated Credentials'
    clientId: botAppId
    scopes: scopes
    parameters: [
      {
        key: 'ClientId'
        value: botAppId
      }
      {
        key: 'UniqueIdentifier'
        value: uniqueIdentifier
      }
      {
        key: 'TokenExchangeUrl'
        value: tokenExchangeUrl
      }
      {
        key: 'TenantId'
        value: tenantId
      }
    ]
  }
}

output connectionName string = connection.name
output connectionId string = connection.id
