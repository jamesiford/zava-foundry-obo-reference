// Azure Bot resource + Teams channel, pointing at the bot web app. Not in the spec's module list but required for
// Teams to reach /api/messages. The bot authenticates as the single-tenant bot app registration; there is no client
// secret (federated credential on the app trusts the managed identity - see Setup-EntraApps.ps1).
//
// Teams SSO: the OAuth connection that lets the bot exchange the Teams SSO token is a separate module
// (botOAuthConnection.bicep) so this one stays about the bot registration and channel.

@description('Azure Bot resource name.')
param botName string

@description('Tags applied to every resource in this module.')
param tags object

@description('Application (client) id of the bot app registration.')
param botAppId string

@description('Entra tenant id.')
param tenantId string

@description('Public HTTPS messaging endpoint, e.g. https://<web app host>/api/messages.')
param messagingEndpoint string

@description('Bot SKU.')
@allowed([
  'F0'
  'S1'
])
param skuName string = 'F0'

resource bot 'Microsoft.BotService/botServices@2022-09-15' = {
  name: botName
  location: 'global'
  tags: tags
  kind: 'azurebot'
  sku: {
    name: skuName
  }
  properties: {
    displayName: botName
    endpoint: messagingEndpoint
    msaAppId: botAppId
    msaAppType: 'SingleTenant'
    msaAppTenantId: tenantId
    disableLocalAuth: true
  }
}

resource teamsChannel 'Microsoft.BotService/botServices/channels@2022-09-15' = {
  parent: bot
  name: 'MsTeamsChannel'
  location: 'global'
  properties: {
    channelName: 'MsTeamsChannel'
    properties: {
      isEnabled: true
    }
  }
}

output botId string = bot.id
output botName string = bot.name
