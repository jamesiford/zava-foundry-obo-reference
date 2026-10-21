#Requires -Version 7.0

<#
.SYNOPSIS
    Creates or updates the Compass bot and Zava Credit Union API Entra app registrations.

.DESCRIPTION
    Idempotent (look up by display name, then create-or-update). Uses Microsoft Graph PowerShell only and never
    creates a client secret; the bot authenticates with a federated identity credential that trusts its managed identity.

    1. Zava Credit Union API app 'zava-credit-union-api-<env>': v2 tokens, identifier URI
       api://zava-credit-union-api-{appId}, delegated scope Member.Read (the scope the bot's OBO exchange requests).
    2. Bot app 'compass-bot-<env>': identifier URI api://botid-{appId}, exposed scope access_as_user,
       the Teams desktop/mobile and web clients pre-authorized for that scope (Teams SSO), a single-page-application
       platform with redirect URIs for the web front door (the deployed origin with -BotEndpoint and, only with
       -LocalPort, http://localhost:<port>/; no secret, PKCE), requested delegated
       permissions Files.Read.All + Sites.Read.All (ids resolved from the Microsoft Graph service principal) and
       Zava API Member.Read and Azure AI Search user_impersonation (for the Foundry IQ knowledge-base route).
    3. Federated identity credentials on the bot app: (a) the Teams SSO credential for the Azure Bot Service token store
       (projected identity subject /eid1/c/pub/t/{tenant}/a/{token store app}/{unique id}), created on the first pass;
       and (b) one trusting the bot's user-assigned managed identity
       (issuer https://login.microsoftonline.com/{tenant}/v2.0, subject = managed identity principal object id,
       audience api://AzureADTokenExchange), skipped until -ManagedIdentityPrincipalId is supplied, because the identity
       only exists after the first `azd provision`; azd's postprovision hook re-runs this script with it.
    4. Service principals for both apps.
    5. Emits an object with the app ids (consumable by `azd env set`) and, optionally, a Teams manifest
       webApplicationInfo fragment.

    Admin consent is a separate, tenant-admin step: see Grant-AdminConsent.ps1.

.PARAMETER Environment
    Compass environment, 'dev' or 'test'. Drives the app display names.

.PARAMETER TenantId
    Entra tenant id to sign in to. Defaults to the tenant of the current Microsoft Graph session.

.PARAMETER ManagedIdentityPrincipalId
    Object (principal) id of the bot's user-assigned managed identity. Required to create the federated credential;
    this is the credential's `subject`.

.PARAMETER ManagedIdentityClientId
    Client id of the bot's managed identity. Informational: recorded in the credential description and the output.

.PARAMETER OutputManifestFragmentPath
    Optional path of a JSON file to write with the Teams manifest `webApplicationInfo` fragment (id + resource).

.PARAMETER BotEndpoint
    Public https origin of the deployed bot (azd output SERVICE_BOT_ENDPOINT). Added as a single-page-application redirect
    URI so the web front door can sign people in. Existing redirect URIs are kept; this one is only added.

.PARAMETER LocalPort
    Local ports to register as http://localhost:<port>/ redirect URIs for running the Host on a developer machine.
    None by default, so a deployed environment does not accept sign-in redirects to a developer's localhost. azd passes
    5240 (the Host's launchSettings.json http port) for the dev environment only.

.EXAMPLE
    ./Setup-EntraApps.ps1 -Environment dev -WhatIf

    Shows what would be created or changed without touching the tenant.

.EXAMPLE
    ./Setup-EntraApps.ps1 -Environment dev -ManagedIdentityPrincipalId $env:BOT_MANAGED_IDENTITY_PRINCIPAL_ID `
        -ManagedIdentityClientId $env:BOT_MANAGED_IDENTITY_CLIENT_ID -OutputManifestFragmentPath ./.generated/teams-webApplicationInfo.json

    Second pass after provisioning: adds the federated identity credential and writes the Teams manifest fragment.

.NOTES
    Requires PowerShell 7, the Microsoft.Graph.Authentication/Applications modules, and a signed-in user who can create
    app registrations (Application.ReadWrite.All; Application Administrator or Cloud Application Administrator, because it also creates the Azure AI Search first-party service principal). No secrets are created, read or logged.
    The Teams client ids below are the well-known Teams SSO client ids documented by Microsoft
    ("Configure Teams tab single sign-on" / bot SSO), identical in every tenant.
    Output properties: BotAppId, BotObjectId, BotIdentifierUri, ZavaApiAppId, ZavaApiObjectId, ZavaApiIdentifierUri,
    TenantId, FederatedCredentialName, TeamsSsoUniqueId, TeamsWebApplicationInfo.
#>
[CmdletBinding(SupportsShouldProcess)]
[OutputType([pscustomobject])]
param(
    [Parameter(Mandatory)]
    [ValidateSet('dev', 'test')]
    [string]$Environment,

    [string]$TenantId,

    [ValidateScript({ $_ -match '^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$' })]
    [string]$ManagedIdentityPrincipalId,

    [ValidateScript({ $_ -match '^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$' })]
    [string]$ManagedIdentityClientId,

    [string]$OutputManifestFragmentPath,

    [ValidateScript({ $_ -match '^https://[^/\s]+/?$' })]
    [string]$BotEndpoint,

    [ValidateRange(1, 65535)]
    [int[]]$LocalPort = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Compass.Infra.Common.ps1')

# Well-known Microsoft Teams client application ids used for Teams SSO pre-authorization (public, per Microsoft docs).
$TeamsDesktopMobileClientId = '1fec8e78-bce4-4aaf-ab1b-5451cc387264'
$TeamsWebClientId = '5e3ce6c0-2b1f-4285-8d4b-75ee78787346'
# Microsoft 365 / Outlook clients, also authorized by the Agents SDK SSO guidance so the app works outside Teams.
$M365WebClientId = '4765445b-32c6-49b0-83e6-1d93765276ca'
$M365DesktopClientId = '0ec893e0-5785-4de6-99da-4ed124e5296c'
$M365MobileClientId = 'd3590ed6-52b3-4102-aeff-aad2292ab01c'
$OutlookWebClientId = 'bc59ab01-8403-45c6-8796-ac3ef710b3e3'
$OutlookMobileClientId = '27922004-5251-4030-b22d-91ecd9a37ea4'
$BotFrameworkOAuthRedirectUri = 'https://token.botframework.com/.auth/web/redirect'
# Bot Service Token Store first-party app id, base64url of its GUID bytes (value published in the Agents SDK docs for
# "AAD v2 with Federated Credentials"). Used to build the projected-identity subject of the Teams SSO credential.
$BotServiceTokenStoreEncodedAppId = '9ExAW52n_ky4ZiS_jhpJIQ'
$BotScopeName = 'access_as_user'
$ZavaScopeName = 'Member.Read'
$GraphDelegatedPermissions = @('Files.Read.All', 'Sites.Read.All')

function ConvertTo-ScopeBody {
    [CmdletBinding()]
    [OutputType([hashtable])]
    param([Parameter(Mandatory)][object]$Scope)

    @{
        id                      = [string]$Scope.Id
        value                   = $Scope.Value
        type                    = $Scope.Type
        isEnabled               = [bool]$Scope.IsEnabled
        adminConsentDisplayName = $Scope.AdminConsentDisplayName
        adminConsentDescription = $Scope.AdminConsentDescription
        userConsentDisplayName  = $Scope.UserConsentDisplayName
        userConsentDescription  = $Scope.UserConsentDescription
    }
}

function Get-ApplicationByName {
    [CmdletBinding()]
    [OutputType([object])]
    param([Parameter(Mandatory)][string]$DisplayName)

    $found = @(Get-MgApplication -Filter "displayName eq '$DisplayName'")
    if ($found.Count -gt 1) {
        throw "Found $($found.Count) app registrations named '$DisplayName'; remove duplicates so the script can stay idempotent."
    }
    if ($found.Count -eq 1) { return $found[0] }
    return $null
}

function Initialize-Application {
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([object])]
    param([Parameter(Mandatory)][string]$DisplayName)

    $app = Get-ApplicationByName -DisplayName $DisplayName
    if ($app) {
        Write-Verbose "App registration '$DisplayName' exists (appId $($app.AppId))."
        return $app
    }

    if ($PSCmdlet.ShouldProcess($DisplayName, 'Create Entra app registration')) {
        Write-Information "Creating app registration '$DisplayName'."
        return New-MgApplication -BodyParameter @{
            displayName    = $DisplayName
            signInAudience = 'AzureADMyOrg'
            api            = @{ requestedAccessTokenVersion = 2 }
        }
    }
    return $null
}

function Sync-ExposedScope {
    # Returns the scope id, adding the scope (and the identifier URI) when missing. Existing scopes are preserved.
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][object]$Application,
        [Parameter(Mandatory)][string]$IdentifierUri,
        [Parameter(Mandatory)][string]$ScopeName,
        [Parameter(Mandatory)][string]$AdminDescription,
        [Parameter(Mandatory)][string]$UserDescription
    )

    $existing = @($Application.Api.Oauth2PermissionScopes)
    $current = $existing | Where-Object { $_.Value -eq $ScopeName } | Select-Object -First 1
    $scopeId = if ($current) { [string]$current.Id } else { [string](New-Guid) }

    $uriOk = $IdentifierUri -in @($Application.IdentifierUris)
    if ($current -and $current.IsEnabled -and $uriOk -and $Application.Api.RequestedAccessTokenVersion -eq 2) {
        Write-Verbose "Scope '$ScopeName' and identifier URI already configured on '$($Application.DisplayName)'."
        return $scopeId
    }

    $scopes = @($existing | Where-Object { $_.Value -ne $ScopeName } | ForEach-Object { ConvertTo-ScopeBody -Scope $_ })
    $scopes += @{
        id                      = $scopeId
        value                   = $ScopeName
        type                    = 'User'
        isEnabled               = $true
        adminConsentDisplayName = $AdminDescription
        adminConsentDescription = $AdminDescription
        userConsentDisplayName  = $UserDescription
        userConsentDescription  = $UserDescription
    }

    if ($PSCmdlet.ShouldProcess($Application.DisplayName, "Set identifier URI $IdentifierUri and expose scope $ScopeName")) {
        Update-MgApplication -ApplicationId $Application.Id -BodyParameter @{
            identifierUris = @($IdentifierUri)
            api            = @{
                requestedAccessTokenVersion = 2
                oauth2PermissionScopes      = $scopes
            }
        }
    }
    return $scopeId
}

function Initialize-ServicePrincipal {
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([object])]
    param([Parameter(Mandatory)][string]$AppId, [Parameter(Mandatory)][string]$DisplayName)

    $sp = Get-CompassServicePrincipal -AppId $AppId
    if ($sp) {
        Write-Verbose "Service principal for '$DisplayName' exists."
        return $sp
    }

    if ($PSCmdlet.ShouldProcess($DisplayName, 'Create service principal')) {
        Write-Information "Creating service principal for '$DisplayName'."
        return New-MgServicePrincipal -AppId $AppId
    }
    return $null
}

function Sync-FederatedCredential {
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][object]$Application,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Issuer,
        [Parameter(Mandatory)][string]$Subject,
        [Parameter(Mandatory)][string]$Description
    )

    $audience = 'api://AzureADTokenExchange'
    $body = @{
        name        = $Name
        issuer      = $Issuer
        subject     = $Subject
        audiences   = @($audience)
        description = $Description
    }

    $existing = Get-MgApplicationFederatedIdentityCredential -ApplicationId $Application.Id |
        Where-Object { $_.Name -eq $Name } | Select-Object -First 1

    if (-not $existing) {
        if ($PSCmdlet.ShouldProcess($Application.DisplayName, "Create federated identity credential '$Name'")) {
            Write-Information "Creating federated identity credential '$Name'."
            New-MgApplicationFederatedIdentityCredential -ApplicationId $Application.Id -BodyParameter $body | Out-Null
        }
        return $Name
    }

    $inSync = $existing.Issuer -eq $Issuer -and $existing.Subject -eq $Subject -and @($existing.Audiences) -contains $audience
    if ($inSync) {
        Write-Verbose "Federated identity credential '$Name' already matches."
        return $Name
    }

    if ($PSCmdlet.ShouldProcess($Application.DisplayName, "Update federated identity credential '$Name'")) {
        Write-Information "Updating federated identity credential '$Name'."
        $update = @{ issuer = $Issuer; subject = $Subject; audiences = @($audience); description = $Description }
        Update-MgApplicationFederatedIdentityCredential -ApplicationId $Application.Id `
            -FederatedIdentityCredentialId $existing.Id -BodyParameter $update | Out-Null
    }
    return $Name
}

Connect-CompassGraph -Scopes @('Application.ReadWrite.All') -TenantId $TenantId
$resolvedTenantId = if ($TenantId) { $TenantId } else { (Get-MgContext).TenantId }
$names = Get-CompassNaming -Environment $Environment

# 1) Zava Credit Union API - created first because the bot requests its Member.Read scope.
$zavaApp = Initialize-Application -DisplayName $names.ZavaApiAppName
$zavaScopeId = $null
$zavaIdentifierUri = $null
if ($zavaApp) {
    $zavaIdentifierUri = "api://zava-credit-union-api-$($zavaApp.AppId)"
    $zavaScopeId = Sync-ExposedScope -Application $zavaApp -IdentifierUri $zavaIdentifierUri -ScopeName $ZavaScopeName `
        -AdminDescription 'Read the signed-in member profile, accounts and transactions on behalf of the user.' `
        -UserDescription 'Let Compass read your Zava Credit Union member data on your behalf.'
    Initialize-ServicePrincipal -AppId $zavaApp.AppId -DisplayName $names.ZavaApiAppName | Out-Null
}

# 2) Bot app registration.
$botApp = Initialize-Application -DisplayName $names.BotAppName
$botIdentifierUri = $null
$ficName = $null
if ($botApp) {
    $botIdentifierUri = "api://botid-$($botApp.AppId)"
    $botScopeId = Sync-ExposedScope -Application $botApp -IdentifierUri $botIdentifierUri -ScopeName $BotScopeName `
        -AdminDescription 'Teams can call the Compass bot on behalf of the signed-in user (SSO).' `
        -UserDescription 'Let Teams sign you in to Compass.'

    $graphPermissions = @(Resolve-GraphDelegatedPermission -Name $GraphDelegatedPermissions)
    $requiredResourceAccess = @(
        @{
            resourceAppId  = $script:MicrosoftGraphAppId
            resourceAccess = @($graphPermissions | ForEach-Object { @{ id = $_.Id; type = 'Scope' } })
        }
    )
    if ($zavaScopeId) {
        $requiredResourceAccess += @{
            resourceAppId  = $zavaApp.AppId
            resourceAccess = @(@{ id = $zavaScopeId; type = 'Scope' })
        }
    }
    $searchPermission = Resolve-AzureSearchDelegatedPermission
    if ($searchPermission) {
        $requiredResourceAccess += @{
            resourceAppId  = $script:AzureSearchAppId
            resourceAccess = @(@{ id = $searchPermission.Id; type = 'Scope' })
        }
    }

    # Refresh after Sync-ExposedScope so the patch below carries the scope that was just written.
    $botApp = Get-ApplicationByName -DisplayName $names.BotAppName
    if ($PSCmdlet.ShouldProcess($names.BotAppName, 'Set pre-authorized Teams clients, requested permissions, redirect URI and SPA redirect URIs')) {
        $preAuthorized = @(
            $TeamsDesktopMobileClientId, $TeamsWebClientId, $M365WebClientId, $M365DesktopClientId,
            $M365MobileClientId, $OutlookWebClientId, $OutlookMobileClientId
        ) | ForEach-Object { @{ appId = $_; delegatedPermissionIds = @($botScopeId) } }
        $scopeBodies = @(@($botApp.Api.Oauth2PermissionScopes) | ForEach-Object { ConvertTo-ScopeBody -Scope $_ })

        # The web front door is a public client (SPA platform, PKCE, no secret) of this registration. Redirect URIs are
        # merged with what is already there so a URI added by hand, or by an earlier deployment, is never dropped.
        $spaRedirectUris = @(@($botApp.Spa.RedirectUris) | Where-Object { $_ })
        if ($LocalPort.Count -gt 0) { $spaRedirectUris += @($LocalPort | ForEach-Object { "http://localhost:$_/" }) }
        if ($BotEndpoint) { $spaRedirectUris += ($BotEndpoint.TrimEnd('/') + '/') }
        $spaRedirectUris = @($spaRedirectUris | Sort-Object -Unique)

        Update-MgApplication -ApplicationId $botApp.Id -BodyParameter @{
            api                    = @{
                requestedAccessTokenVersion = 2
                oauth2PermissionScopes      = $scopeBodies
                preAuthorizedApplications   = $preAuthorized
            }
            requiredResourceAccess = $requiredResourceAccess
            web                    = @{ redirectUris = @($BotFrameworkOAuthRedirectUri) }
            spa                    = @{ redirectUris = $spaRedirectUris }
        }
    }

    Initialize-ServicePrincipal -AppId $botApp.AppId -DisplayName $names.BotAppName | Out-Null

    # 3) Federated identity credentials (no client secret anywhere).
    # 3a) Teams SSO: the Azure Bot OAuth connection (infra/modules/botOAuthConnection.bicep) authenticates to this app as the
    #     Bot Service Token Store using a projected identity, so the credential subject is
    #     /eid1/c/pub/t/<base64url tenant>/a/<base64url token-store app>/<unique id>. The unique id must equal the
    #     connection's UniqueIdentifier. Independent of the managed identity, so it is created on the first pass.
    $encodedTenant = [Convert]::ToBase64String(([guid]$resolvedTenantId).ToByteArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    $ssoSubject = "/eid1/c/pub/t/$encodedTenant/a/$BotServiceTokenStoreEncodedAppId/$($names.TeamsSsoUniqueId)"
    $null = Sync-FederatedCredential -Application $botApp -Name $names.TeamsSsoFederatedCredential `
        -Issuer "https://login.microsoftonline.com/$resolvedTenantId/v2.0" `
        -Subject $ssoSubject -Description 'Lets the Azure Bot Service token store exchange Teams SSO tokens for this app'

    # 3b) The bot's managed identity, used by the Agents SDK ServiceConnection.
    if ($ManagedIdentityPrincipalId) {
        $description = 'Trusts the Compass bot user-assigned managed identity'
        if ($ManagedIdentityClientId) { $description += " (clientId $ManagedIdentityClientId)" }
        $ficName = Sync-FederatedCredential -Application $botApp -Name $names.FederatedCredential `
            -Issuer "https://login.microsoftonline.com/$resolvedTenantId/v2.0" `
            -Subject $ManagedIdentityPrincipalId -Description $description
    }
    else {
        Write-Warning 'ManagedIdentityPrincipalId not supplied: federated identity credential skipped. Re-run after `azd provision` with the bot managed identity ids.'
    }
}

$teamsFragment = $null
if ($botApp) {
    $teamsFragment = [ordered]@{
        webApplicationInfo = [ordered]@{
            id       = $botApp.AppId
            resource = $botIdentifierUri
        }
    }
    if ($OutputManifestFragmentPath -and $PSCmdlet.ShouldProcess($OutputManifestFragmentPath, 'Write Teams manifest webApplicationInfo fragment')) {
        $directory = Split-Path -Path $OutputManifestFragmentPath -Parent
        if ($directory -and -not (Test-Path -Path $directory)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }
        $teamsFragment | ConvertTo-Json -Depth 5 | Set-Content -Path $OutputManifestFragmentPath -Encoding utf8
        Write-Information "Wrote Teams manifest fragment to $OutputManifestFragmentPath"
    }
}

[pscustomobject]@{
    Environment             = $Environment
    TenantId                = $resolvedTenantId
    BotAppId                = if ($botApp) { $botApp.AppId } else { $null }
    BotObjectId             = if ($botApp) { $botApp.Id } else { $null }
    BotIdentifierUri        = $botIdentifierUri
    ZavaApiAppId            = if ($zavaApp) { $zavaApp.AppId } else { $null }
    ZavaApiObjectId         = if ($zavaApp) { $zavaApp.Id } else { $null }
    ZavaApiIdentifierUri    = $zavaIdentifierUri
    FederatedCredentialName = $ficName
    TeamsSsoUniqueId        = $names.TeamsSsoUniqueId
    TeamsWebApplicationInfo = $teamsFragment
}
