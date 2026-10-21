// Azure AI Search service that hosts the Foundry IQ knowledge base and its remote SharePoint knowledge source.
//
// ARM covers only the service. The knowledge *source*, knowledge *base* and the Foundry project connection are
// data-plane / preview objects and are created by scripts/Setup-KnowledgeBase.ps1 (see infra/README.md).
//
// Keyless on purpose: local (API key) auth is disabled so the only credentials are Entra tokens - the person's
// delegated Search token at query time, and RBAC roles for the workloads. A key would bypass per-person trimming.

@description('Search service name (globally unique, 2-60 chars, lowercase letters/digits/hyphens).')
@minLength(2)
@maxLength(60)
param searchName string

@description('Azure region.')
param location string

@description('Tags applied to the service.')
param tags object

@description('Pricing tier. Basic is enough for a reference build; the remote SharePoint source needs no index storage.')
@allowed([
  'basic'
  'standard'
])
param skuName string = 'basic'

@description('Public network access for the service.')
@allowed([
  'enabled'
  'disabled'
])
param publicNetworkAccess string = 'enabled'

resource search 'Microsoft.Search/searchServices@2025-05-01' = {
  name: searchName
  location: location
  tags: tags
  sku: {
    name: skuName
  }
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'Default'
    publicNetworkAccess: publicNetworkAccess
    disableLocalAuth: true
    semanticSearch: 'free'
  }
}

output searchName string = search.name
output searchId string = search.id
output searchEndpoint string = 'https://${search.name}.search.windows.net'
output searchPrincipalId string = search.identity.principalId
