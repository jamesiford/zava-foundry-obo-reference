#Requires -Version 7.0

<#
.SYNOPSIS
    Read-only check that this machine has what the build, the local run and the deployment need.

.DESCRIPTION
    Creates and changes NOTHING. It looks for tools and PowerShell modules, and asks the Azure CLI and the Azure
    Developer CLI whether you are signed in (from their local caches; it prints no tokens and no tenant or subscription ids).

    Each row says whether the item is Required for the chosen Mode, Optional, or only needed for one script, and how to
    install it when it is missing. It writes one result object per check and fails (non-zero exit) only when a Required item
    for the chosen Mode is missing, so it can gate a deployment script.

.PARAMETER Mode
    Local  : what you need to build, test and run locally (Aspire needs Docker and the Functions Core Tools).
    Deploy : what you need to provision and deploy to Azure and run the Entra and data scripts.
    All    : both (default).

.EXAMPLE
    ./Test-Prerequisites.ps1 -Mode Deploy | Format-Table -AutoSize

.NOTES
    Output: Area, Item, Status, Required, Version, Detail, Install.
    Status is Ok, Missing or Unknown. Required is the Mode that needs it, or Optional.
#>
[CmdletBinding()]
[OutputType([pscustomobject[]])]
param(
    [ValidateSet('Local', 'Deploy', 'All')]
    [string]$Mode = 'All'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$results = [System.Collections.Generic.List[pscustomobject]]::new()

function Add-Result {
    param(
        [string]$Area,
        [string]$Item,
        [ValidateSet('Ok', 'Missing', 'Unknown')]
        [string]$Status,
        [ValidateSet('Local', 'Deploy', 'Optional')]
        [string]$Required,
        [string]$Version = '',
        [string]$Detail = '',
        [string]$Install = ''
    )
    $results.Add([pscustomobject]@{
            Area     = $Area
            Item     = $Item
            Status   = $Status
            Required = $Required
            Version  = $Version
            Detail   = $Detail
            Install  = $Install
        })
}

function Get-FirstLine {
    param([scriptblock]$Command)
    try {
        $out = & $Command 2>$null
        return (($out | Select-Object -First 1) -as [string])
    }
    catch {
        return $null
    }
}

# ---- PowerShell itself
Add-Result -Area 'Shell' -Item 'PowerShell 7+' -Status 'Ok' -Required 'Local' -Version $PSVersionTable.PSVersion.ToString()

# ---- .NET SDK
$dotnet = Get-Command -Name dotnet -ErrorAction SilentlyContinue
if ($dotnet) {
    $sdk = Get-FirstLine { dotnet --version }
    $major = if ($sdk -match '^(\d+)\.') { [int]$Matches[1] } else { 0 }
    if ($major -ge 10) {
        Add-Result -Area 'Build' -Item '.NET SDK 10 or later' -Status 'Ok' -Required 'Local' -Version $sdk
    }
    else {
        Add-Result -Area 'Build' -Item '.NET SDK 10 or later' -Status 'Missing' -Required 'Local' -Version $sdk -Detail 'The projects target net10.0.' -Install 'https://dotnet.microsoft.com/download'
    }
}
else {
    Add-Result -Area 'Build' -Item '.NET SDK 10 or later' -Status 'Missing' -Required 'Local' -Detail 'dotnet was not found on PATH.' -Install 'https://dotnet.microsoft.com/download'
}

# ---- Command-line tools
$tools = @(
    @{ Name = 'az'; Item = 'Azure CLI'; Required = 'Deploy'; Version = { (az version --output json 2>$null | ConvertFrom-Json).'azure-cli' }; Install = 'https://learn.microsoft.com/cli/azure/install-azure-cli'; Detail = 'Used by azd, Set-MemberMapping.ps1 and Setup-KnowledgeBase.ps1.' },
    @{ Name = 'azd'; Item = 'Azure Developer CLI'; Required = 'Deploy'; Version = { Get-FirstLine { azd version } }; Install = 'https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd'; Detail = 'Runs provision, the pre/post-provision hooks and deploy.' },
    @{ Name = 'docker'; Item = 'Docker'; Required = 'Optional'; Version = { Get-FirstLine { docker --version } }; Install = 'https://docs.docker.com/get-docker/'; Detail = 'Only to run the Aspire AppHost locally (Azurite runs in a container).' },
    @{ Name = 'func'; Item = 'Azure Functions Core Tools'; Required = 'Optional'; Version = { Get-FirstLine { func --version } }; Install = 'https://learn.microsoft.com/azure/azure-functions/functions-run-local'; Detail = 'Only to run the MCP Functions host locally under Aspire.' }
)
foreach ($tool in $tools) {
    $cmd = Get-Command -Name $tool.Name -ErrorAction SilentlyContinue
    if ($cmd) {
        $v = & $tool.Version
        Add-Result -Area 'Tools' -Item $tool.Item -Status 'Ok' -Required $tool.Required -Version ([string]$v) -Detail $tool.Detail
    }
    else {
        Add-Result -Area 'Tools' -Item $tool.Item -Status 'Missing' -Required $tool.Required -Detail $tool.Detail -Install $tool.Install
    }
}

# ---- PowerShell modules
$modules = @(
    @{ Name = 'Microsoft.Graph.Authentication'; Required = 'Deploy'; Detail = 'Entra scripts (Setup-EntraApps, Grant-AdminConsent, Assert-DemoUsers, Set-MemberMapping).' },
    @{ Name = 'Microsoft.Graph.Applications'; Required = 'Deploy'; Detail = 'Entra scripts.' },
    @{ Name = 'Microsoft.Graph.Identity.SignIns'; Required = 'Deploy'; Detail = 'Entra scripts.' },
    @{ Name = 'Microsoft.Graph.Users'; Required = 'Deploy'; Detail = 'Entra scripts.' },
    @{ Name = 'Az.Accounts'; Required = 'Optional'; Detail = 'Setup-KnowledgeBase.ps1 can use it instead of the Azure CLI; New-FoundryImpersonationRole.ps1 needs it.' },
    @{ Name = 'Az.Resources'; Required = 'Optional'; Detail = 'Only for the New-FoundryImpersonationRole.ps1 fallback.' },
    @{ Name = 'PnP.PowerShell'; Required = 'Optional'; Detail = 'Only for Seed-SharePointTestData.ps1 (SharePoint unique permissions).' }
)
foreach ($module in $modules) {
    $found = Get-Module -ListAvailable -Name $module.Name | Sort-Object Version -Descending | Select-Object -First 1
    if ($found) {
        $detail = $module.Detail
        # A module installed under OneDrive syncs its help files and has triggered data-loss-prevention alerts.
        if ($found.ModuleBase -match '\\OneDrive[^\\]*\\') {
            $detail = "$detail Installed under a OneDrive folder: install tooling outside synced folders."
        }
        Add-Result -Area 'Modules' -Item $module.Name -Status 'Ok' -Required $module.Required -Version $found.Version.ToString() -Detail $detail
    }
    else {
        Add-Result -Area 'Modules' -Item $module.Name -Status 'Missing' -Required $module.Required -Detail $module.Detail -Install "Install-Module $($module.Name) -Scope AllUsers (or CurrentUser outside a synced folder)"
    }
}

# ---- Sign-in state (local caches only; nothing is printed that identifies a tenant)
if (Get-Command -Name az -ErrorAction SilentlyContinue) {
    $account = Get-FirstLine { az account show --query name --output tsv }
    if ($account) {
        Add-Result -Area 'Sign-in' -Item 'Azure CLI signed in' -Status 'Ok' -Required 'Deploy' -Detail 'A subscription is selected.'
    }
    else {
        Add-Result -Area 'Sign-in' -Item 'Azure CLI signed in' -Status 'Missing' -Required 'Deploy' -Install 'az login'
    }
}
if (Get-Command -Name azd -ErrorAction SilentlyContinue) {
    $status = Get-FirstLine { azd auth login --check-status }
    if ($status -match '(?i)logged in') {
        Add-Result -Area 'Sign-in' -Item 'azd signed in' -Status 'Ok' -Required 'Deploy'
    }
    elseif ($status) {
        Add-Result -Area 'Sign-in' -Item 'azd signed in' -Status 'Missing' -Required 'Deploy' -Install 'azd auth login'
    }
    else {
        Add-Result -Area 'Sign-in' -Item 'azd signed in' -Status 'Unknown' -Required 'Deploy' -Detail 'Could not read the status; run: azd auth login --check-status'
    }
}

$results

$blocking = $results | Where-Object {
    $_.Status -eq 'Missing' -and ($_.Required -eq $Mode -or ($Mode -eq 'All' -and $_.Required -in 'Local', 'Deploy'))
}
if ($blocking) {
    Write-Warning ("Missing required items for mode '{0}': {1}" -f $Mode, (($blocking | ForEach-Object Item) -join ', '))
    exit 1
}
