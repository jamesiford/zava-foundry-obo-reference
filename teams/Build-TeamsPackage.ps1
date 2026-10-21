#Requires -Version 7.0
<#
.SYNOPSIS
    Builds the sideloadable Teams app package (manifest.json + color.png + outline.png in a zip).

.DESCRIPTION
    Stamps teams/manifest.json with the bot app id and the webApplicationInfo block that
    infra/scripts/Setup-EntraApps.ps1 wrote to infra/.generated/teams-webApplicationInfo.json, generates the two
    required icons (192x192 colour, 32x32 white-on-transparent outline) in memory, and zips the result. Nothing in
    the package is tenant-specific beyond that generated fragment, and the output folder is git-ignored.

    The icons are rasterised and PNG-encoded by this script (no System.Drawing / libgdiplus), so it runs unchanged on
    Windows, macOS and Linux with only PowerShell 7.

.PARAMETER ManifestFragmentPath
    Output of Setup-EntraApps.ps1 -OutputManifestFragmentPath: { "webApplicationInfo": { "id", "resource" } }.

.PARAMETER TemplatePath
    The manifest template. Placeholders: {{BOT_APP_ID}} and {{BOT_IDENTIFIER_URI}}.

.PARAMETER OutputPath
    Zip to write. Defaults to teams/dist/Compass-Teams-App.zip.

.EXAMPLE
    ./teams/Build-TeamsPackage.ps1
    Then in Teams: Apps > Manage your apps > Upload an app > Upload a custom app > Compass-Teams-App.zip.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ManifestFragmentPath = (Join-Path $PSScriptRoot '..' 'infra' '.generated' 'teams-webApplicationInfo.json'),
    [string]$TemplatePath = (Join-Path $PSScriptRoot 'manifest.json'),
    [string]$OutputPath = (Join-Path $PSScriptRoot 'dist' 'Compass-Teams-App.zip')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression

function Get-WebApplicationInfo {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -Path $Path -PathType Leaf)) {
        throw "Manifest fragment not found at '$Path'. Run infra/scripts/Setup-EntraApps.ps1 with -OutputManifestFragmentPath first."
    }
    $info = (Get-Content -Path $Path -Raw | ConvertFrom-Json).webApplicationInfo
    if (-not $info -or -not $info.id -or -not $info.resource) {
        throw "'$Path' has no webApplicationInfo.id / webApplicationInfo.resource."
    }
    if ($info.id -notmatch '^[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$') {
        throw 'webApplicationInfo.id is not a GUID.'
    }
    if ($info.resource -ne "api://botid-$($info.id)") {
        throw "webApplicationInfo.resource must be api://botid-<id> so the Teams SSO token audience matches the bot's exposed API."
    }
    $info
}

function Get-StampedManifest {
    param([Parameter(Mandatory)][string]$Template, [Parameter(Mandatory)]$WebApplicationInfo)

    $text = (Get-Content -Path $Template -Raw).
        Replace('{{BOT_APP_ID}}', $WebApplicationInfo.id).
        Replace('{{BOT_IDENTIFIER_URI}}', $WebApplicationInfo.resource)
    if ($text -match '\{\{[A-Z_]+\}\}') {
        throw "Unresolved placeholder $($Matches[0]) left in the manifest template."
    }
    # Round-trips through the parser so a template typo fails here rather than in the Teams upload dialog.
    ($text | ConvertFrom-Json) | ConvertTo-Json -Depth 10
}

# Compass glyph: a ring with a diamond needle. Returns true where the glyph is drawn; u and v run -1..1.
function Test-GlyphPixel {
    param([double]$U, [double]$V)

    $radius = [math]::Sqrt($U * $U + $V * $V)
    if ($radius -ge 0.60 -and $radius -le 0.74) { return $true }
    $along = ($U + $V) / [math]::Sqrt(2)
    $across = ($U - $V) / [math]::Sqrt(2)
    ([math]::Abs($across) / 0.15 + [math]::Abs($along) / 0.46) -le 1
}

$script:Crc32Table = [uint32[]]::new(256)
for ($n = 0; $n -lt 256; $n++) {
    [uint32]$c = $n
    for ($k = 0; $k -lt 8; $k++) {
        $c = if ($c -band 1) { [uint32]3988292384 -bxor ([uint32]($c -shr 1)) } else { [uint32]($c -shr 1) }
    }
    $script:Crc32Table[$n] = $c
}

function Get-Crc32 {
    param([byte[]]$Bytes)

    [uint32]$crc = [uint32]4294967295
    foreach ($byte in $Bytes) {
        $crc = $script:Crc32Table[($crc -bxor $byte) -band 255] -bxor ([uint32]($crc -shr 8))
    }
    [uint32]($crc -bxor [uint32]4294967295)
}

function ConvertTo-BigEndianByteArray {
    param([uint32]$Value)
    [byte[]]@((($Value -shr 24) -band 255), (($Value -shr 16) -band 255), (($Value -shr 8) -band 255), ($Value -band 255))
}

function Get-PngChunk {
    param([string]$Type, [byte[]]$Data)

    $typeAndData = [byte[]]([System.Text.Encoding]::ASCII.GetBytes($Type) + $Data)
    [byte[]]((ConvertTo-BigEndianByteArray ([uint32]$Data.Length)) + $typeAndData + (ConvertTo-BigEndianByteArray (Get-Crc32 $typeAndData)))
}

function Get-IconPng {
    param(
        [Parameter(Mandatory)][int]$Size,
        [Parameter(Mandatory)][ValidateSet('Color', 'Outline')][string]$Kind
    )

    $samples = if ($Size -le 64) { 4 } else { 2 }
    $raw = [System.IO.MemoryStream]::new()
    for ($y = 0; $y -lt $Size; $y++) {
        $raw.WriteByte(0) # PNG filter type: none
        for ($x = 0; $x -lt $Size; $x++) {
            $hits = 0
            for ($sy = 0; $sy -lt $samples; $sy++) {
                for ($sx = 0; $sx -lt $samples; $sx++) {
                    $u = 2 * ($x + ($sx + 0.5) / $samples) / $Size - 1
                    $v = 2 * ($y + ($sy + 0.5) / $samples) / $Size - 1
                    if (Test-GlyphPixel -U $u -V $v) { $hits++ }
                }
            }
            $coverage = $hits / ($samples * $samples)
            if ($Kind -eq 'Outline') {
                $pixel = [byte[]]@(255, 255, 255, [byte][math]::Round(255 * $coverage))
            }
            else {
                # Over the brand blue (#0B5CAD), the glyph is white.
                $pixel = [byte[]]@(
                    [byte][math]::Round(11 + (255 - 11) * $coverage),
                    [byte][math]::Round(92 + (255 - 92) * $coverage),
                    [byte][math]::Round(173 + (255 - 173) * $coverage),
                    255)
            }
            $raw.Write($pixel, 0, 4)
        }
    }

    $compressed = [System.IO.MemoryStream]::new()
    $zlib = [System.IO.Compression.ZLibStream]::new($compressed, [System.IO.Compression.CompressionLevel]::Optimal, $true)
    $raw.Position = 0
    $raw.CopyTo($zlib)
    $zlib.Dispose()

    $header = [byte[]]((ConvertTo-BigEndianByteArray ([uint32]$Size)) + (ConvertTo-BigEndianByteArray ([uint32]$Size)) + @(8, 6, 0, 0, 0))
    [byte[]](
        @(137, 80, 78, 71, 13, 10, 26, 10) +
        (Get-PngChunk 'IHDR' $header) +
        (Get-PngChunk 'IDAT' $compressed.ToArray()) +
        (Get-PngChunk 'IEND' ([byte[]]@())))
}

function Write-ZipEntry {
    param([System.IO.Compression.ZipArchive]$Archive, [string]$Name, [byte[]]$Content)

    $entry = $Archive.CreateEntry($Name, [System.IO.Compression.CompressionLevel]::Optimal)
    $stream = $entry.Open()
    try { $stream.Write($Content, 0, $Content.Length) } finally { $stream.Dispose() }
}

$webApplicationInfo = Get-WebApplicationInfo -Path $ManifestFragmentPath
$manifestJson = Get-StampedManifest -Template $TemplatePath -WebApplicationInfo $webApplicationInfo
$colorIcon = Get-IconPng -Size 192 -Kind Color
$outlineIcon = Get-IconPng -Size 32 -Kind Outline
Write-Information "Stamped manifest for bot app $($webApplicationInfo.id); icons: color $($colorIcon.Length) B, outline $($outlineIcon.Length) B."

if ($PSCmdlet.ShouldProcess($OutputPath, 'Write Teams app package')) {
    $directory = Split-Path -Path $OutputPath -Parent
    if ($directory -and -not (Test-Path -Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    if (Test-Path -Path $OutputPath) { Remove-Item -Path $OutputPath -Force }

    $file = [System.IO.File]::Open($OutputPath, [System.IO.FileMode]::CreateNew)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new($file, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            Write-ZipEntry -Archive $archive -Name 'manifest.json' -Content ([System.Text.UTF8Encoding]::new($false).GetBytes($manifestJson))
            Write-ZipEntry -Archive $archive -Name 'color.png' -Content $colorIcon
            Write-ZipEntry -Archive $archive -Name 'outline.png' -Content $outlineIcon
        }
        finally { $archive.Dispose() }
    }
    finally { $file.Dispose() }
    Write-Information "Wrote $OutputPath"
}

[pscustomobject]@{
    BotAppId = $webApplicationInfo.id
    Package  = $OutputPath
}
