#Requires -Version 7.0

<#
.SYNOPSIS
    Seeds the three demo document libraries (with deliberately different permissions) from infra/demo-data.

.DESCRIPTION
    Idempotent. In the target site it creates, if missing, three libraries with UNIQUE permissions (nothing copied from the
    site's Members/Visitors groups):
      - 'Lending Policy'            readable by Daniel Park (User A) only
      - 'Portfolio and Collections' readable by Maya Lin (User B) only
      - 'Member Services FAQ'       readable by both (the shared control library)
    and uploads each Markdown file under infra/demo-data/<folder> as a real Word document (.docx), converted in memory. The
    Copilot Retrieval API only does semantic/hybrid retrieval for .docx/.pptx/.pdf/.aspx/.one; other extensions get lexical
    matching only, so uploading raw .md would make the demo answers weaker and less representative.

    Existing files are skipped unless -Force is given. Nothing is written to disk and nothing secret is used.

    Why PnP.PowerShell: Microsoft Graph has no API to break permission inheritance on a library, so unique permissions use
    SharePoint's own API through PnP.PowerShell.

.PARAMETER SiteUrl
    URL of the EXISTING SharePoint site, e.g. https://contoso.sharepoint.com/sites/compass-demo. This script does not create
    the site.

.PARAMETER TenantDomain
    Verified domain the demo users sign in with, e.g. contoso.onmicrosoft.com. Used to build both UPNs.

.PARAMETER PnPClientId
    Client id of an Entra app registration allowed for interactive PnP.PowerShell sign-in (required by current PnP.PowerShell
    versions). Omit only if your PnP.PowerShell version does not require it.

.PARAMETER DeviceLogin
    Sign in with a device code instead of an interactive browser window, for shells that cannot show one.

.PARAMETER Force
    Re-upload documents that already exist (overwrites them).

.EXAMPLE
    ./Seed-SharePointTestData.ps1 -SiteUrl https://contoso.sharepoint.com/sites/compass-demo -TenantDomain contoso.onmicrosoft.com -WhatIf

    Shows the libraries, permissions and documents that would be created.

.NOTES
    Requires PnP.PowerShell and a signed-in user who is a Site Owner (or has Manage Web). SharePoint adds Limited Access on the
    site automatically when library-level permission is granted. The Copilot index can take from about 15 minutes to an hour
    to pick up new files; see infra/demo-data/README.md.
#>
[CmdletBinding(SupportsShouldProcess)]
[OutputType([pscustomobject[]])]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^https://[A-Za-z0-9.-]+\.sharepoint\.com(/.*)?$')]
    [string]$SiteUrl,

    [Parameter(Mandatory)]
    [ValidatePattern('^(?!-)[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)+$')]
    [string]$TenantDomain,

    [string]$PnPClientId,

    [switch]$DeviceLogin,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Compass.Infra.Common.ps1')
. (Join-Path $PSScriptRoot 'Compass.DemoDocuments.ps1')

$demoDataRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'demo-data'

function Initialize-DemoLibrary {
    [CmdletBinding(SupportsShouldProcess)]
    [OutputType([pscustomobject[]])]
    param(
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][string]$UrlName,
        [Parameter(Mandatory)][string]$SourceFolder,
        [Parameter(Mandatory)][string[]]$Readers,
        [switch]$Force
    )

    $list = Get-PnPList -Identity $Title -Includes HasUniqueRoleAssignments -ErrorAction SilentlyContinue
    if (-not $list -and $PSCmdlet.ShouldProcess($Title, 'Create document library')) {
        Write-Information "Creating library '$Title'."
        $null = New-PnPList -Title $Title -Url $UrlName -Template DocumentLibrary -OnQuickLaunch
        $list = Get-PnPList -Identity $Title -Includes HasUniqueRoleAssignments
    }

    if ($list) {
        if (-not $list.HasUniqueRoleAssignments -and $PSCmdlet.ShouldProcess($Title, 'Break permission inheritance (no copied assignments)')) {
            Set-PnPList -Identity $Title -BreakRoleInheritance -CopyRoleAssignments:$false -ClearSubscopes
        }
        foreach ($reader in $Readers) {
            if ($PSCmdlet.ShouldProcess($Title, "Grant Read to $reader")) {
                Set-PnPListPermission -Identity $Title -User $reader -AddRole 'Read'
            }
        }
    }

    foreach ($source in Get-ChildItem -Path (Join-Path $demoDataRoot $SourceFolder) -Filter '*.md' -File) {
        $fileName = "$($source.BaseName).docx"
        $fileUrl = "$UrlName/$fileName"

        $exists = $false
        if ($list) {
            # Some PnP.PowerShell versions return $null for a missing file instead of throwing, so test the result.
            $existing = Get-PnPFile -Url $fileUrl -AsListItem -ErrorAction SilentlyContinue
            $exists = $null -ne $existing
        }

        $action = if ($exists -and -not $Force) { 'Skipped (exists)' } else { 'Uploaded' }
        if ($action -eq 'Uploaded' -and $PSCmdlet.ShouldProcess($fileUrl, 'Convert Markdown to .docx and upload')) {
            $bytes = ConvertTo-DocxByteArray -Lines (Get-Content -LiteralPath $source.FullName -Encoding utf8)
            $stream = [System.IO.MemoryStream]::new($bytes)
            try {
                $null = Add-PnPFile -Stream $stream -FileName $fileName -Folder $UrlName
            }
            finally {
                $stream.Dispose()
            }
        }
        elseif ($action -eq 'Uploaded') {
            $action = 'WhatIf'
        }

        [pscustomobject]@{
            Library = $Title
            File    = $fileName
            Readers = $Readers -join ', '
            Action  = $action
        }
    }
}

if (-not (Get-Module -ListAvailable -Name PnP.PowerShell)) {
    throw 'Module PnP.PowerShell is required. Install-Module PnP.PowerShell -Scope CurrentUser'
}

$people = Get-CompassDemoUser -TenantDomain $TenantDomain
$userA = ($people | Where-Object Key -EQ 'A').UserPrincipalName
$userB = ($people | Where-Object Key -EQ 'B').UserPrincipalName

$connectArgs = @{ Url = $SiteUrl }
if ($DeviceLogin) { $connectArgs['DeviceLogin'] = $true } else { $connectArgs['Interactive'] = $true }
if ($PnPClientId) { $connectArgs['ClientId'] = $PnPClientId }
Connect-PnPOnline @connectArgs

$libraries = @(
    @{ Title = 'Lending Policy'; UrlName = 'LendingPolicy'; SourceFolder = 'lending-policy'; Readers = @($userA) }
    @{ Title = 'Portfolio and Collections'; UrlName = 'PortfolioAndCollections'; SourceFolder = 'portfolio-and-collections'; Readers = @($userB) }
    @{ Title = 'Member Services FAQ'; UrlName = 'MemberServicesFAQ'; SourceFolder = 'member-services-faq'; Readers = @($userA, $userB) }
)

foreach ($library in $libraries) {
    Initialize-DemoLibrary @library -Force:$Force
}
