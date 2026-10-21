// Key Vault using Azure RBAC authorization. No secrets are created here: the Compass identity chain is
// secretless (federated credential + managed identities). The vault exists for future/optional settings only.

@description('Key Vault name (3-24 chars, globally unique).')
@minLength(3)
@maxLength(24)
param keyVaultName string

@description('Azure region.')
param location string

@description('Tags applied to every resource in this module.')
param tags object

@description('Entra tenant that owns the vault.')
param tenantId string

@description('Enable purge protection. Irreversible once enabled; leave false for disposable dev/test environments.')
param enablePurgeProtection bool = false

@description('Soft-delete retention in days.')
@minValue(7)
@maxValue(90)
param softDeleteRetentionInDays int = 7

resource keyVault 'Microsoft.KeyVault/vaults@2025-05-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: softDeleteRetentionInDays
    // The API rejects an explicit false for purge protection, so only send it when enabled.
    enablePurgeProtection: enablePurgeProtection ? true : null
    publicNetworkAccess: 'Enabled'
  }
}

output keyVaultId string = keyVault.id
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
