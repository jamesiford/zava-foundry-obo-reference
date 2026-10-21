#Requires -Version 7.0

<#
.SYNOPSIS
    Creates the "Compass Agent Impersonator" custom role and assigns it to the bot managed identity per hosted agent.

.DESCRIPTION
    The role grants one Foundry data action,
    Microsoft.CognitiveServices/accounts/AIServices/agents/endpoints/UserIdentityImpersonation/action, which lets the
    bot send x-ms-user-identity so each end user gets an isolated agent session. End users never receive this role.
    Idempotent: the role definition and each assignment are checked (Get-AzRoleDefinition / Get-AzRoleAssignment)
    before being created.

    The role is named "Compass Agent Impersonator (<env>)" - the same name infra/modules/rbac.bicep uses - so if the Bicep
    deployment already created it, this script finds it and only fills in missing assignments.

    Bicep vs. script: rbac.bicep performs the same work declaratively and is the default path. This script is the
    fallback, and the route for per-agent scopes when the agent application resources
    (Microsoft.CognitiveServices/accounts/projects/applications) are not managed by Bicep in your environment
    (for example agents created through the Foundry data plane only). Per-agent scope here is
    <account>/projects/<project>/applications/<agent>.

.PARAMETER Environment
    Compass environment, 'dev' or 'test'. Part of the role name.

.PARAMETER FoundryAccountResourceId
    Full resource id of the Foundry (AIServices) account; used as the role's assignable scope.

.PARAMETER ProjectName
    Foundry project name that contains the agent applications.

.PARAMETER BotManagedIdentityPrincipalId
    Object (principal) id of the bot's user-assigned managed identity (azd output BOT_MANAGED_IDENTITY_PRINCIPAL_ID).

.PARAMETER AgentNames
    Hosted agent names to scope assignments to. Defaults to SharePointRetrievalAgent and ZavaApiAgent.

.PARAMETER AssignAtProjectScope
    Assign at the Foundry project instead of per agent. Broader than intended; use only if the per-agent scope cannot be
    resolved in your environment.

.PARAMETER AgentConsumerRoleDefinitionId
    Optional built-in role definition GUID that lets the bot invoke agents (for example "Foundry User"), assigned at the
    same scope as the impersonation role. Omit to skip.

.EXAMPLE
    ./New-FoundryImpersonationRole.ps1 -Environment dev -FoundryAccountResourceId $env:FOUNDRY_ACCOUNT_ID `
        -ProjectName $env:FOUNDRY_PROJECT_NAME -BotManagedIdentityPrincipalId $env:BOT_MANAGED_IDENTITY_PRINCIPAL_ID -WhatIf

    Previews the role and per-agent assignments for the default agents.

.EXAMPLE
    ./New-FoundryImpersonationRole.ps1 -Environment test -FoundryAccountResourceId $id -ProjectName proj-compass-test `
        -BotManagedIdentityPrincipalId $mi -AgentNames 'SharePointRetrievalAgent','ZavaApiAgent' -Verbose

    Creates the role if missing and assigns it per agent.

.NOTES
    Requires Az.Accounts and Az.Resources, an active Connect-AzAccount session, and Owner or User Access Administrator
    (Microsoft.Authorization/roleDefinitions/write and roleAssignments/write) on the Foundry account.
    Custom role and assignment propagation can take a minute; assignments are retried briefly. No secrets are used.
#>
[CmdletBinding(SupportsShouldProcess)]
[OutputType([pscustomobject[]])]
param(
    [Parameter(Mandatory)]
    [ValidateSet('dev', 'test')]
    [string]$Environment,

    [Parameter(Mandatory)]
    [ValidatePattern('^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\.CognitiveServices/accounts/[^/]+$')]
    [string]$FoundryAccountResourceId,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ProjectName,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$')]
    [string]$BotManagedIdentityPrincipalId,

    [ValidateNotNullOrEmpty()]
    [string[]]$AgentNames = @('SharePointRetrievalAgent', 'ZavaApiAgent'),

    [switch]$AssignAtProjectScope,

    [ValidatePattern('^[0-9a-fA-F]{8}-([0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}$')]
    [string]$AgentConsumerRoleDefinitionId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Compass.Infra.Common.ps1')

$ImpersonationDataAction = 'Microsoft.CognitiveServices/accounts/AIServices/agents/endpoints/UserIdentityImpersonation/action'

function Initialize-ImpersonatorRole {
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([object])]
    param(
        [Parameter(Mandatory)][string]$RoleName,
        [Parameter(Mandatory)][string]$AssignableScope
    )

    $role = Get-AzRoleDefinition -Name $RoleName
    if ($role) {
        Write-Verbose "Role definition '$RoleName' exists ($($role.Id))."
        if ($ImpersonationDataAction -notin @($role.DataActions)) {
            Write-Warning "Role '$RoleName' exists but lacks $ImpersonationDataAction; update it manually or remove it and re-run."
        }
        return $role
    }

    if ($PSCmdlet.ShouldProcess($RoleName, 'Create custom role definition')) {
        Write-Information "Creating custom role '$RoleName'."
        $definition = Get-AzRoleDefinition -Name 'Reader'
        $definition.Id = $null
        $definition.IsCustom = $true
        $definition.Name = $RoleName
        $definition.Description = 'Lets the Compass bot pass x-ms-user-identity to a Foundry agent endpoint so sessions are isolated per end user.'
        $definition.Actions.Clear()
        $definition.NotActions.Clear()
        $definition.DataActions.Clear()
        $definition.DataActions.Add($ImpersonationDataAction)
        $definition.AssignableScopes.Clear()
        $definition.AssignableScopes.Add($AssignableScope)
        return New-AzRoleDefinition -Role $definition
    }
    return $null
}

function Sync-RoleAssignment {
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)][string]$PrincipalId,
        [Parameter(Mandatory)][string]$RoleDefinitionId,
        [Parameter(Mandatory)][string]$RoleLabel,
        [Parameter(Mandatory)][string]$Scope
    )

    $existing = @(Get-AzRoleAssignment -ObjectId $PrincipalId -RoleDefinitionId $RoleDefinitionId -Scope $Scope |
            Where-Object { $_.Scope -eq $Scope })
    if ($existing.Count -gt 0) {
        Write-Verbose "'$RoleLabel' already assigned at $Scope."
        return [pscustomobject]@{ Role = $RoleLabel; Scope = $Scope; Result = 'unchanged' }
    }

    if ($PSCmdlet.ShouldProcess($Scope, "Assign '$RoleLabel' to $PrincipalId")) {
        # A new custom role can take a short while to become assignable.
        for ($attempt = 1; $attempt -le 6; $attempt++) {
            try {
                New-AzRoleAssignment -ObjectId $PrincipalId -RoleDefinitionId $RoleDefinitionId -Scope $Scope `
                    -ObjectType ServicePrincipal | Out-Null
                break
            }
            catch {
                if ($attempt -eq 6 -or $_.Exception.Message -notmatch 'RoleDefinitionDoesNotExist|PrincipalNotFound|does not exist') { throw }
                Write-Verbose "Assignment not ready (attempt $attempt); waiting for propagation."
                Start-Sleep -Seconds 10
            }
        }
    }
    return [pscustomobject]@{ Role = $RoleLabel; Scope = $Scope; Result = 'assigned' }
}

foreach ($module in 'Az.Accounts', 'Az.Resources') {
    if (-not (Get-Module -ListAvailable -Name $module)) {
        throw "Module $module is required. Install-Module $module -Scope CurrentUser"
    }
}
if (-not (Get-AzContext)) {
    throw 'No Azure session. Run Connect-AzAccount (as Owner or User Access Administrator) first.'
}

$roleName = (Get-CompassNaming -Environment $Environment).ImpersonatorRoleName
$projectScope = "$FoundryAccountResourceId/projects/$ProjectName"
$scopes = if ($AssignAtProjectScope) {
    @($projectScope)
}
else {
    @($AgentNames | ForEach-Object { "$projectScope/applications/$_" })
}

$role = Initialize-ImpersonatorRole -RoleName $roleName -AssignableScope $FoundryAccountResourceId

foreach ($scope in $scopes) {
    if ($role) {
        Sync-RoleAssignment -PrincipalId $BotManagedIdentityPrincipalId -RoleDefinitionId $role.Id `
            -RoleLabel $roleName -Scope $scope
    }
    else {
        Write-Information "What if: would assign '$roleName' to $BotManagedIdentityPrincipalId at $scope once the role exists."
    }
    if ($AgentConsumerRoleDefinitionId) {
        Sync-RoleAssignment -PrincipalId $BotManagedIdentityPrincipalId -RoleDefinitionId $AgentConsumerRoleDefinitionId `
            -RoleLabel "built-in agent consumer ($AgentConsumerRoleDefinitionId)" -Scope $scope
    }
}
