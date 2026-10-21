#Requires -Version 7.0
<#
.SYNOPSIS
    Creates the Foundry IQ remote-SharePoint knowledge source, the knowledge base, and the Foundry project connection
    that the SharePointKnowledgeAgent's MCP tool uses.

.DESCRIPTION
    These objects are Azure AI Search / Foundry DATA-PLANE preview resources, not ARM resources, so they cannot live in
    Bicep (the Search service itself is infra/modules/aiSearch.bicep). Every call is a PUT, so the script is idempotent.

    The knowledge source is `remoteSharePoint`: nothing is indexed or copied. At query time Azure AI Search calls
    Microsoft Graph / SharePoint with the CALLER's delegated token (x-ms-query-source-authorization), which is how a
    person only ever retrieves content they could already open. The workload credentials used here and by the project
    connection (the Foundry project managed identity) never widen that.

    Run once per environment after `azd provision`, before publishing the agent
    (`dotnet run --project src/Bot/Compass.Bot.Host -- --publish-knowledge-agent`).

.PARAMETER SearchServiceName
    Azure AI Search service name (output AZURE_SEARCH_NAME).

.PARAMETER FoundryProjectResourceId
    ARM id of the Foundry project (Microsoft.CognitiveServices/accounts/<account>/projects/<project>).

.PARAMETER KnowledgeBaseName
    Knowledge base name. Must match Bot:Foundry:KnowledgeAgent:KnowledgeBaseMcpEndpoint.

.PARAMETER KnowledgeSourceName
    Remote SharePoint knowledge source name.

.PARAMETER ConnectionName
    Foundry project connection name. Must match Bot:Foundry:KnowledgeAgent:ProjectConnectionName.

.PARAMETER PortalConnectionName
    Name of the AI Search connection that makes the knowledge base appear on the Foundry portal's Knowledge page.

.PARAMETER FoundryAccountName
    Foundry (AIServices) account hosting the model. Used only when -ModelDeployment is set.

.PARAMETER ModelDeployment
    Optional model deployment for answer synthesis / query planning. Omit for retrieval-only. When set, the Search
    service identity needs Cognitive Services User on the account (granted by infra/modules/rbac.bicep).

.PARAMETER SharePointFilter
    Optional KQL filter limiting which SharePoint content the source may return (for example a site path).

.PARAMETER ApiVersion
    Search data-plane API version. The remote SharePoint source and knowledge bases are preview features.

.EXAMPLE
    ./Setup-KnowledgeBase.ps1 -SearchServiceName srch-compass-dev-abc123 `
        -FoundryProjectResourceId /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<acct>/projects/<proj> `
        -FoundryAccountName <acct> -ModelDeployment gpt-5.6-sol -WhatIf

.NOTES
    Requires Az.Accounts (Connect-AzAccount). Holds and prints no secrets; tokens stay in memory.
    Caller needs Search Service Contributor on the service and Contributor on the Foundry project.
    The person querying needs a Microsoft 365 Copilot licence or the pay-as-you-go Retrieval API; Microsoft
    documents a 200 requests per user per hour limit for remote SharePoint.
    Not executed against a live tenant in this build (the request bodies were checked line by line against the
    2026-08-01-preview "remote SharePoint knowledge source", "knowledge base" and "Connect agents to Foundry IQ"
    articles, and `-WhatIf` lists every call without sending it).
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,58}[a-z0-9]$')]
    [string]$SearchServiceName,

    [Parameter(Mandatory)]
    [ValidatePattern('^/subscriptions/[^/]+/resourceGroups/[^/]+/providers/Microsoft\.CognitiveServices/accounts/[^/]+/projects/[^/]+$')]
    [string]$FoundryProjectResourceId,

    [ValidateNotNullOrEmpty()]
    [string]$KnowledgeBaseName = 'zava-kb',

    [ValidateNotNullOrEmpty()]
    [string]$KnowledgeSourceName = 'zava-sharepoint',

    [ValidateNotNullOrEmpty()]
    [string]$ConnectionName = 'compass-knowledge-base',
    [string]$PortalConnectionName = 'zava-foundry-iq-search',

    [string]$FoundryAccountName,

    [string]$ModelDeployment,

    [string]$SharePointFilter,

    [string]$ApiVersion = '2026-08-01-preview'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$searchEndpoint = "https://$SearchServiceName.search.windows.net"

function Get-CompassBearerToken {
    <#
    .SYNOPSIS
        Gets an Entra access token for a resource using the signed-in Az context.
    #>
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][string]$ResourceUrl)

    if (Get-Command -Name Get-AzAccessToken -ErrorAction SilentlyContinue) {
        $token = (Get-AzAccessToken -ResourceUrl $ResourceUrl -AsSecureString).Token
        return (ConvertFrom-SecureString -SecureString $token -AsPlainText)
    }

    # No Az.Accounts: fall back to the Azure CLI's signed-in session.
    if (Get-Command -Name az -ErrorAction SilentlyContinue) {
        $cliToken = az account get-access-token --resource $ResourceUrl --query accessToken -o tsv
        if ($LASTEXITCODE -eq 0 -and $cliToken) { return $cliToken }
    }

    throw 'Neither Az.Accounts nor a signed-in Azure CLI is available. Run: Install-Module Az.Accounts -Scope CurrentUser; Connect-AzAccount (or: az login)'
}

function Invoke-CompassPut {
    <#
    .SYNOPSIS
        PUTs a JSON body (idempotent create-or-update) and returns the response, honouring -WhatIf.
    #>
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([object])]
    param(
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][string]$Token,
        [Parameter(Mandatory)][hashtable]$Body,
        [Parameter(Mandatory)][string]$Description
    )

    if (-not $PSCmdlet.ShouldProcess($Uri, $Description)) { return $null }

    Invoke-RestMethod -Method Put -Uri $Uri -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 20) `
        -Headers @{ Authorization = "Bearer $Token" }
}

$searchToken = if ($WhatIfPreference) { 'whatif' } else { Get-CompassBearerToken -ResourceUrl 'https://search.azure.com' }
$armToken = if ($WhatIfPreference) { 'whatif' } else { Get-CompassBearerToken -ResourceUrl 'https://management.azure.com' }

# 1. Remote SharePoint knowledge source. No index, no ingestion: retrieval happens live, as the caller.
$sourceParameters = @{
    resourceMetadata = @('Author', 'Title')
    containerTypeId  = $null
}
if ($SharePointFilter) { $sourceParameters['filterExpression'] = $SharePointFilter }

$sourceBody = @{
    name                       = $KnowledgeSourceName
    kind                       = 'remoteSharePoint'
    description                = 'Zava SharePoint content, retrieved live with the calling person''s delegated token.'
    encryptionKey              = $null
    remoteSharePointParameters = $sourceParameters
}
Invoke-CompassPut -Uri "$searchEndpoint/knowledgesources/${KnowledgeSourceName}?api-version=$ApiVersion" `
    -Token $searchToken -Body $sourceBody -Description 'Create or update remote SharePoint knowledge source' | Out-Null

# 2. Knowledge base. The model is optional for non-web sources; omit it for pure retrieval.
$baseBody = @{
    name                 = $KnowledgeBaseName
    description          = 'Zava Compass knowledge base over SharePoint, trimmed per person by the caller token.'
    retrievalInstructions = 'Search SharePoint for member-service policy, procedure and playbook documents relevant to the question.'
    answerInstructions   = 'Answer only from retrieved documents and cite each source. Say so when nothing relevant is found.'
    outputMode           = 'answerSynthesis'
    knowledgeSources     = @(@{ name = $KnowledgeSourceName })
    encryptionKey        = $null
    retrievalReasoningEffort = @{ kind = 'auto' }
    models               = @()
}
if ($ModelDeployment) {
    if (-not $FoundryAccountName) { throw '-FoundryAccountName is required with -ModelDeployment.' }
    $baseBody['models'] = @(@{
            kind                  = 'azureOpenAI'
            azureOpenAIParameters = @{
                resourceUri  = "https://$FoundryAccountName.openai.azure.com"
                deploymentId = $ModelDeployment
                modelName    = $ModelDeployment
            }
        })
}
else {
    $baseBody['outputMode'] = 'extractiveData'
    $baseBody.Remove('answerInstructions')
}
Invoke-CompassPut -Uri "$searchEndpoint/knowledgebases/${KnowledgeBaseName}?api-version=$ApiVersion" `
    -Token $searchToken -Body $baseBody -Description 'Create or update knowledge base' | Out-Null

# 3. Foundry project connection. Authenticates the agent's MCP tool to Search as the project managed identity;
#    the person's identity still travels separately in x-ms-query-source-authorization.
$connectionBody = @{
    name       = $ConnectionName
    type       = 'Microsoft.MachineLearningServices/workspaces/connections'
    properties = @{
        authType     = 'ProjectManagedIdentity'
        category     = 'RemoteTool'
        target       = "$searchEndpoint/knowledgebases/$KnowledgeBaseName/mcp?api-version=$ApiVersion"
        isSharedToAll = $true
        audience     = 'https://search.azure.com/'
        metadata     = @{ ApiType = 'Azure' }
    }
}
Invoke-CompassPut -Uri "https://management.azure.com$FoundryProjectResourceId/connections/${ConnectionName}?api-version=2025-10-01-preview" `
    -Token $armToken -Body $connectionBody -Description 'Create or update Foundry project MCP connection' | Out-Null

# 4. The Foundry portal's Knowledge page (Foundry IQ) lists knowledge bases only for an AI Search resource connected to
#    the project, and the MCP connection above is a different kind of connection that the page does not show. This one
#    uses Entra auth (the service has key auth disabled); whoever opens the page also needs Search Index Data Reader and
#    Search Service Contributor on the service. The agent does not use this connection.
$searchResourceId = ($FoundryProjectResourceId -replace '/providers/Microsoft\.CognitiveServices/.*$', "/providers/Microsoft.Search/searchServices/$SearchServiceName")
$portalConnectionBody = @{
    name       = $PortalConnectionName
    type       = 'Microsoft.MachineLearningServices/workspaces/connections'
    properties = @{
        authType      = 'AAD'
        category      = 'CognitiveSearch'
        target        = $searchEndpoint
        isSharedToAll = $true
        metadata      = @{ ApiType = 'Azure'; ResourceId = $searchResourceId; ApiVersion = '2025-05-01' }
    }
}
Invoke-CompassPut -Uri "https://management.azure.com$FoundryProjectResourceId/connections/${PortalConnectionName}?api-version=2025-10-01-preview" `
    -Token $armToken -Body $portalConnectionBody -Description 'Create or update Foundry project AI Search connection (Knowledge page)' | Out-Null

[pscustomobject]@{
    KnowledgeSource     = $KnowledgeSourceName
    KnowledgeBase       = $KnowledgeBaseName
    Connection          = $ConnectionName
    PortalConnection    = $PortalConnectionName
    KnowledgeBaseMcpUri = "$searchEndpoint/knowledgebases/$KnowledgeBaseName/mcp?api-version=$ApiVersion"
}
