#Requires -Version 7.0

<#
.SYNOPSIS
    Grants tenant-wide (admin) delegated consent to the Compass bot for Microsoft Graph and the Zava Credit Union API.

.DESCRIPTION
    Run once per tenant by an administrator after Setup-EntraApps.ps1. For the bot service principal it creates
    oauth2PermissionGrants with consentType AllPrincipals for:
      - Microsoft Graph: Files.Read.All, Sites.Read.All
      - Zava Credit Union API: Member.Read
      - Azure AI Search: user_impersonation (the Search-scope OBO token for the Foundry IQ knowledge base)
      - The bot itself: access_as_user (the web front door signs in as a public client of the same registration, so
        consenting for everyone avoids a consent prompt on the page)
    Idempotent: existing grants for the same client/resource pair are updated in place (missing scopes are added;
    scopes granted by other means are kept) rather than duplicated, and an up-to-date grant is left untouched.
    Fails with remediation guidance when the caller lacks DelegatedPermissionGrant.ReadWrite.All or an admin role.

.PARAMETER Environment
    Compass environment, 'dev' or 'test'. Selects the 'compass-bot-<env>' and 'zava-credit-union-api-<env>' apps.

.PARAMETER TenantId
    Entra tenant id to sign in to. Defaults to the tenant of the current Microsoft Graph session.

.EXAMPLE
    ./Grant-AdminConsent.ps1 -Environment dev -WhatIf

    Lists the grants that would be created or updated.

.EXAMPLE
    ./Grant-AdminConsent.ps1 -Environment test -Verbose

    Grants consent for the test environment (run as Global Administrator or Privileged Role Administrator).

.NOTES
    Requires the Microsoft.Graph.Authentication, Microsoft.Graph.Applications and Microsoft.Graph.Identity.SignIns modules.
    The signed-in user needs the delegated DelegatedPermissionGrant.ReadWrite.All Graph scope and an Entra role that can
    consent for the tenant (Global Administrator, Privileged Role Administrator, or Cloud Application Administrator for
    non-Graph resources). No secrets are used.
#>
[CmdletBinding(SupportsShouldProcess)]
[OutputType([pscustomobject[]])]
param(
    [Parameter(Mandatory)]
    [ValidateSet('dev', 'test')]
    [string]$Environment,

    [string]$TenantId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Compass.Infra.Common.ps1')

function Get-ServicePrincipalByName {
    [CmdletBinding()]
    [OutputType([object])]
    param([Parameter(Mandatory)][string]$DisplayName)

    $found = @(Get-MgServicePrincipal -Filter "displayName eq '$DisplayName'")
    if ($found.Count -ne 1) {
        throw "Expected exactly one service principal named '$DisplayName' but found $($found.Count). Run Setup-EntraApps.ps1 -Environment $Environment first."
    }
    return $found[0]
}

function Sync-DelegatedGrant {
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)][object]$ClientServicePrincipal,
        [Parameter(Mandatory)][object]$ResourceServicePrincipal,
        [Parameter(Mandatory)][string[]]$Scope
    )

    $filter = "clientId eq '$($ClientServicePrincipal.Id)' and resourceId eq '$($ResourceServicePrincipal.Id)' and consentType eq 'AllPrincipals'"
    $existing = @(Get-MgOauth2PermissionGrant -Filter $filter)
    $label = "$($ClientServicePrincipal.DisplayName) -> $($ResourceServicePrincipal.DisplayName)"

    try {
        if ($existing.Count -eq 0) {
            $action = 'created'
            if ($PSCmdlet.ShouldProcess($label, "Grant admin consent: $($Scope -join ' ')")) {
                New-MgOauth2PermissionGrant -BodyParameter @{
                    clientId    = $ClientServicePrincipal.Id
                    consentType = 'AllPrincipals'
                    resourceId  = $ResourceServicePrincipal.Id
                    scope       = ($Scope -join ' ')
                } | Out-Null
            }
        }
        else {
            $grant = $existing[0]
            $granted = @($grant.Scope -split '\s+' | Where-Object { $_ })
            $missing = @($Scope | Where-Object { $_ -notin $granted })
            if ($missing.Count -eq 0) {
                $action = 'unchanged'
            }
            else {
                $action = 'updated'
                $merged = (@($granted) + $missing) -join ' '
                if ($PSCmdlet.ShouldProcess($label, "Add scopes to existing grant: $($missing -join ' ')")) {
                    Update-MgOauth2PermissionGrant -OAuth2PermissionGrantId $grant.Id -BodyParameter @{ scope = $merged } | Out-Null
                }
            }
        }
    }
    catch {
        if ($_.Exception.Message -match 'Forbidden|Authorization_RequestDenied|Insufficient privileges|403') {
            throw "Not authorized to grant admin consent for '$label'. Sign in as a Global Administrator or Privileged Role Administrator and consent to the Graph scope DelegatedPermissionGrant.ReadWrite.All (Connect-MgGraph -Scopes DelegatedPermissionGrant.ReadWrite.All). Original error: $($_.Exception.Message)"
        }
        throw
    }

    [pscustomobject]@{
        Client   = $ClientServicePrincipal.DisplayName
        Resource = $ResourceServicePrincipal.DisplayName
        Scope    = ($Scope -join ' ')
        Result   = $action
    }
}

Connect-CompassGraph -Scopes @('DelegatedPermissionGrant.ReadWrite.All', 'Application.Read.All') -TenantId $TenantId

$context = Get-MgContext
if ('DelegatedPermissionGrant.ReadWrite.All' -notin $context.Scopes) {
    throw 'The Microsoft Graph session lacks DelegatedPermissionGrant.ReadWrite.All. Reconnect with: Connect-MgGraph -Scopes DelegatedPermissionGrant.ReadWrite.All,Application.Read.All as a tenant administrator.'
}

$names = Get-CompassNaming -Environment $Environment
$botSp = Get-ServicePrincipalByName -DisplayName $names.BotAppName
$zavaSp = Get-ServicePrincipalByName -DisplayName $names.ZavaApiAppName
$graphSp = Get-CompassServicePrincipal -AppId $script:MicrosoftGraphAppId
if (-not $graphSp) {
    throw 'The Microsoft Graph service principal was not found in this tenant.'
}

Sync-DelegatedGrant -ClientServicePrincipal $botSp -ResourceServicePrincipal $graphSp -Scope @('Files.Read.All', 'Sites.Read.All')
Sync-DelegatedGrant -ClientServicePrincipal $botSp -ResourceServicePrincipal $zavaSp -Scope @('Member.Read')
# The web front door is a public client of this same registration, so it asks for the bot's own access_as_user scope.
# Consenting for everyone up front means the page never shows a consent prompt (Teams is pre-authorized instead).
Sync-DelegatedGrant -ClientServicePrincipal $botSp -ResourceServicePrincipal $botSp -Scope @('access_as_user')

$searchSp = Get-AzureSearchServicePrincipal
if ($searchSp) {
    Sync-DelegatedGrant -ClientServicePrincipal $botSp -ResourceServicePrincipal $searchSp -Scope @('user_impersonation')
}
