#Requires -Version 7.0

<#
.SYNOPSIS
    Maps the two demo people's Entra object ids to their seeded member numbers on the bot's App Service.

.DESCRIPTION
    The bot resolves "who is this person" to a credit-union member number from configuration (Bot:Members:ByObjectId, spec
    D11). Object ids are tenant data, so none live in source: this script looks each person up by UPN in Microsoft Graph at
    run time and writes one App Service app setting per person, Bot__Members__ByObjectId__<objectId> = <memberNumber>,
    through the Azure CLI. Existing settings are left alone; re-running is idempotent.

    The Azure CLI echoes the full settings list (including secrets) after 'appsettings set' unless output is suppressed,
    so this script always passes --output none. It prints only the UPN and member number it mapped, never an object id.

.PARAMETER TenantDomain
    Verified domain the demo users sign in with, e.g. contoso.onmicrosoft.com.

.PARAMETER ResourceGroup
    Resource group that contains the bot App Service.

.PARAMETER WebAppName
    Name of the bot App Service (azd output SERVICE_BOT_RESOURCE_NAME).

.PARAMETER TenantId
    Optional Entra tenant id to sign in to for the Graph lookup.

.EXAMPLE
    ./Set-MemberMapping.ps1 -TenantDomain contoso.onmicrosoft.com -ResourceGroup rg-compass-dev -WebAppName app-compass-dev-abc123 -WhatIf

    Resolves the two people (read-only) and shows the settings it would write.

.NOTES
    Requires Microsoft.Graph.Authentication and Microsoft.Graph.Users (User.Read.All, read only) and a signed-in Azure CLI
    ('az login') with permission to change the web app's settings. Restart the web app afterwards so the bot reloads them.
#>
[CmdletBinding(SupportsShouldProcess)]
[OutputType([pscustomobject[]])]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(?!-)[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$')]
    [string]$TenantDomain,

    [Parameter(Mandatory)]
    [string]$ResourceGroup,

    [Parameter(Mandatory)]
    [string]$WebAppName,

    [string]$TenantId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Compass.Infra.Common.ps1')

Connect-CompassGraph -Scopes @('User.Read.All') -TenantId $TenantId

$settings = [System.Collections.Generic.List[string]]::new()
$mapped = foreach ($person in Get-CompassDemoUser -TenantDomain $TenantDomain) {
    $user = Get-MgUser -UserId $person.UserPrincipalName -Property 'id' -ErrorAction SilentlyContinue
    if (-not $user) {
        throw "User '$($person.UserPrincipalName)' was not found. Run Assert-DemoUsers.ps1 first."
    }

    # Linux App Service rejects hyphens in setting names; the bot looks the object id up with and without them.
    $settings.Add("Bot__Members__ByObjectId__$($user.Id.Replace('-', ''))=$($person.MemberNumber)")
    [pscustomobject]@{
        Person            = $person.DisplayName
        UserPrincipalName = $person.UserPrincipalName
        MemberNumber      = $person.MemberNumber
        WebApp            = $WebAppName
    }
}

if ($PSCmdlet.ShouldProcess($WebAppName, "Set $($settings.Count) member-mapping app settings")) {
    if (-not (Get-Command -Name az -ErrorAction SilentlyContinue)) {
        throw 'The Azure CLI (az) is required. Install it and run: az login'
    }

    & az webapp config appsettings set --resource-group $ResourceGroup --name $WebAppName --settings @settings --output none
    if ($LASTEXITCODE -ne 0) {
        throw "az webapp config appsettings set failed with exit code $LASTEXITCODE."
    }
}

$mapped
