#Requires -Version 7.0

<#
.SYNOPSIS
    Read-only check that the two demo people exist, are enabled, and hold the Microsoft 365 Copilot and Teams licences.

.DESCRIPTION
    Replaces the old user-seeding script. The demo people already exist in the sandbox tenant, so this script creates and
    changes NOTHING: no user, group, password, licence or Conditional Access policy. It only reads, and it fails (non-zero
    exit through a terminating error) when any check fails, so it can gate the rest of the demo setup.

    Checks per person: the account exists, AccountEnabled is true, a Teams service plan (TEAMS1) is provisioned, and a
    Microsoft 365 Copilot service plan (M365_COPILOT_*) is provisioned. Copilot matters because the Copilot Retrieval API
    that backs the SharePoint route is licensed per user.

.PARAMETER TenantDomain
    Verified domain the demo users sign in with, e.g. contoso.onmicrosoft.com. Never committed to source.

.PARAMETER TenantId
    Optional Entra tenant id to sign in to. Defaults to the tenant of the current Microsoft Graph session.

.EXAMPLE
    ./Assert-DemoUsers.ps1 -TenantDomain contoso.onmicrosoft.com

    Prints one result per person and throws if either is missing, disabled or unlicensed.

.NOTES
    Requires Microsoft.Graph.Authentication and Microsoft.Graph.Users, and the delegated scope User.Read.All (read only).
    Output: Key, DisplayName, UserPrincipalName, Exists, Enabled, HasTeams, HasCopilot, Passed.
#>
[CmdletBinding()]
[OutputType([pscustomobject[]])]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(?!-)[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$')]
    [string]$TenantDomain,

    [string]$TenantId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Compass.Infra.Common.ps1')

function Get-DemoUserStatus {
    [CmdletBinding()]
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)][pscustomobject]$Person
    )

    $user = Get-MgUser -UserId $Person.UserPrincipalName -Property 'id,displayName,accountEnabled,userPrincipalName' -ErrorAction SilentlyContinue
    $exists = $null -ne $user
    $enabled = $exists -and [bool]$user.AccountEnabled

    $plans = @()
    if ($exists) {
        $plans = @(Get-MgUserLicenseDetail -UserId $Person.UserPrincipalName |
                ForEach-Object { $_.ServicePlans } |
                Where-Object { $_.ProvisioningStatus -eq 'Success' } |
                ForEach-Object { $_.ServicePlanName })
    }

    $hasTeams = 'TEAMS1' -in $plans
    $hasCopilot = @($plans | Where-Object { $_ -like 'M365_COPILOT*' }).Count -gt 0

    [pscustomobject]@{
        Key               = $Person.Key
        DisplayName       = $Person.DisplayName
        UserPrincipalName = $Person.UserPrincipalName
        Exists            = $exists
        Enabled           = $enabled
        HasTeams          = $hasTeams
        HasCopilot        = $hasCopilot
        Passed            = $exists -and $enabled -and $hasTeams -and $hasCopilot
    }
}

Connect-CompassGraph -Scopes @('User.Read.All') -TenantId $TenantId

$results = @(Get-CompassDemoUser -TenantDomain $TenantDomain | ForEach-Object { Get-DemoUserStatus -Person $_ })
$results

$failed = @($results | Where-Object { -not $_.Passed })
if ($failed.Count -gt 0) {
    $names = ($failed | ForEach-Object { "$($_.DisplayName) <$($_.UserPrincipalName)>" }) -join '; '
    throw "Demo user check failed for: $names. Each person must exist, be enabled, and hold Teams and Microsoft 365 Copilot licences."
}
