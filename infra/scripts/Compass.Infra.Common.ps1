#Requires -Version 7.0
<#
.SYNOPSIS
    Shared helpers dot-sourced by the Compass infrastructure scripts (naming, Graph connection, permission lookup).

.DESCRIPTION
    Keeps names and Microsoft Graph plumbing in one place (DRY, spec section 6) so Setup-EntraApps.ps1,
    Grant-AdminConsent.ps1, Assert-DemoUsers.ps1, Set-MemberMapping.ps1 and Seed-SharePointTestData.ps1 cannot drift apart.
    Dot-source it; it defines functions only and performs no work on load.

.NOTES
    Holds no secrets. Nothing here is executed against a tenant until a calling script invokes a function.
#>

Set-StrictMode -Version Latest

# Well-known first-party application ids (public values, identical in every tenant).
$script:MicrosoftGraphAppId = '00000003-0000-0000-c000-000000000000'
# Azure AI Search (https://search.azure.com). The knowledge-base route trims results with the caller's delegated
# token for this resource, so the bot app needs its user_impersonation permission for the OBO exchange.
$script:AzureSearchAppId = '880da380-985e-4198-81b9-e05b1cc53158'

function Get-CompassNaming {
    <#
    .SYNOPSIS
        Returns the display names derived from the environment name.
    .PARAMETER Environment
        Compass environment, 'dev' or 'test'.
    #>
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('dev', 'test')]
        [string]$Environment
    )

    [pscustomobject]@{
        BotAppName            = "compass-bot-$Environment"
        ZavaApiAppName        = "zava-credit-union-api-$Environment"
        FederatedCredential   = "compass-bot-mi-$Environment"
        TeamsSsoFederatedCredential = "compass-bot-teams-sso-$Environment"
        TeamsSsoUniqueId      = "compass-teams-sso-$Environment"
        ImpersonatorRoleName  = "Compass Agent Impersonator ($Environment)"
    }
}

function Get-CompassDemoUser {
    <#
    .SYNOPSIS
        Returns the two existing demo people (User A and User B) with their user principal names.
    .DESCRIPTION
        The people already exist in the sandbox tenant; nothing here creates them. Only the UPN local parts and the
        member numbers live in source - the tenant domain is always supplied by the caller, and object ids are resolved
        from Microsoft Graph at run time, so no tenant-identifying value is ever committed.
    .PARAMETER TenantDomain
        Verified domain the demo users sign in with, e.g. contoso.onmicrosoft.com.
    #>
    [CmdletBinding()]
    [OutputType([pscustomobject[]])]
    param(
        [Parameter(Mandatory)]
        [ValidatePattern('^(?!-)[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$')]
        [string]$TenantDomain
    )

    @(
        [pscustomobject]@{
            Key               = 'A'
            DisplayName       = 'Daniel Park'
            Role              = 'Credit Officer'
            UserPrincipalName = "daniel.park@$TenantDomain"
            MemberNumber      = 'ZCU-100234'
        }
        [pscustomobject]@{
            Key               = 'B'
            DisplayName       = 'Maya Lin'
            Role              = 'Portfolio Analyst - West'
            UserPrincipalName = "maya.lin@$TenantDomain"
            MemberNumber      = 'ZCU-100871'
        }
    )
}

function Connect-CompassGraph {
    <#
    .SYNOPSIS
        Ensures a Microsoft Graph PowerShell session with the required delegated scopes exists.
    .DESCRIPTION
        Reuses the current session when it already has every scope; otherwise signs in interactively with the
        requested scopes (a human must run this - no app secret or stored credential is involved).
    .PARAMETER Scopes
        Delegated Graph scopes the calling script needs.
    .PARAMETER TenantId
        Optional tenant to sign in to.
    #>
    [CmdletBinding()]
    [OutputType([void])]
    param(
        [Parameter(Mandatory)]
        [string[]]$Scopes,

        [string]$TenantId
    )

    if (-not (Get-Command -Name Connect-MgGraph -ErrorAction SilentlyContinue)) {
        throw 'Microsoft Graph PowerShell is not installed. Run: Install-Module Microsoft.Graph.Authentication, Microsoft.Graph.Applications, Microsoft.Graph.Identity.SignIns, Microsoft.Graph.Users -Scope CurrentUser'
    }

    # Headless shells (CI, terminals with no window handle for WAM) can supply a token issued to a signed-in human,
    # e.g. COMPASS_GRAPH_ACCESS_TOKEN=$(az account get-access-token --resource-type ms-graph --query accessToken -o tsv).
    if ($env:COMPASS_GRAPH_ACCESS_TOKEN) {
        Connect-MgGraph -AccessToken (ConvertTo-SecureString -String $env:COMPASS_GRAPH_ACCESS_TOKEN -AsPlainText -Force) -NoWelcome | Out-Null
        return
    }

    $context = Get-MgContext
    $missing = @($Scopes)
    if ($context) {
        $missing = @($Scopes | Where-Object { $_ -notin $context.Scopes })
    }

    if ($missing.Count -gt 0) {
        Write-Verbose "Connecting to Microsoft Graph with scopes: $($Scopes -join ', ')"
        $connectArgs = @{ Scopes = $Scopes; NoWelcome = $true }
        if ($TenantId) { $connectArgs['TenantId'] = $TenantId }
        Connect-MgGraph @connectArgs | Out-Null
    }
}

function Get-CompassServicePrincipal {
    <#
    .SYNOPSIS
        Returns the service principal for an application id, or $null when none exists.
    .PARAMETER AppId
        Application (client) id.
    #>
    [CmdletBinding()]
    [OutputType([object])]
    param(
        [Parameter(Mandatory)]
        [string]$AppId
    )

    $found = @(Get-MgServicePrincipal -Filter "appId eq '$AppId'")
    if ($found.Count -gt 1) {
        throw "Found $($found.Count) service principals for appId $AppId; expected at most one."
    }
    if ($found.Count -eq 1) { return $found[0] }
    return $null
}

function Resolve-GraphDelegatedPermission {
    <#
    .SYNOPSIS
        Resolves delegated (OAuth2) permission names on the Microsoft Graph service principal to their ids.
    .DESCRIPTION
        Ids are read from the tenant's Microsoft Graph service principal instead of being hardcoded.
    .PARAMETER Name
        Permission values such as Files.Read.All.
    #>
    [CmdletBinding()]
    [OutputType([pscustomobject[]])]
    param(
        [Parameter(Mandatory)]
        [string[]]$Name
    )

    $graphSp = Get-CompassServicePrincipal -AppId $script:MicrosoftGraphAppId
    if (-not $graphSp) {
        throw 'The Microsoft Graph service principal was not found in this tenant.'
    }

    foreach ($permission in $Name) {
        $scope = $graphSp.Oauth2PermissionScopes | Where-Object { $_.Value -eq $permission } | Select-Object -First 1
        if (-not $scope) {
            throw "Delegated permission '$permission' was not found on the Microsoft Graph service principal."
        }
        [pscustomobject]@{ Name = $permission; Id = [string]$scope.Id }
    }
}

function Get-AzureSearchServicePrincipal {
    <#
    .SYNOPSIS
        Returns the Azure AI Search service principal, creating it in the tenant when it does not exist yet.
    .DESCRIPTION
        The first-party Search service principal is only materialized in a tenant once something requests it, so a
        fresh tenant may need it created before a permission can be resolved or consented.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([object])]
    param()

    $sp = Get-CompassServicePrincipal -AppId $script:AzureSearchAppId
    if (-not $sp -and $PSCmdlet.ShouldProcess('Azure AI Search', 'Create the first-party service principal')) {
        $sp = New-MgServicePrincipal -BodyParameter @{ appId = $script:AzureSearchAppId }
    }
    return $sp
}

function Resolve-AzureSearchDelegatedPermission {
    <#
    .SYNOPSIS
        Resolves the Azure AI Search delegated permission id (user_impersonation) from the tenant's service principal.
    #>
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [string]$Name = 'user_impersonation'
    )

    $searchSp = Get-AzureSearchServicePrincipal
    if (-not $searchSp) {
        return $null
    }
    $scope = $searchSp.Oauth2PermissionScopes | Where-Object { $_.Value -eq $Name } | Select-Object -First 1
    if (-not $scope) {
        throw "Delegated permission '$Name' was not found on the Azure AI Search service principal."
    }
    [pscustomobject]@{ Name = $Name; Id = [string]$scope.Id }
}
