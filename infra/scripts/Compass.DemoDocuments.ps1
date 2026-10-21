#Requires -Version 7.0
<#
.SYNOPSIS
    Converts the small Markdown subset used by infra/demo-data into a real .docx, entirely in memory.

.DESCRIPTION
    The Copilot Retrieval API only does semantic/hybrid retrieval for .docx, .pptx, .pdf, .aspx and .one; .md and .txt get
    lexical matching only. So the seed script uploads genuine Word documents. Supported Markdown: '# ' and '## ' headings,
    '- ' bullets, pipe tables (the separator row is skipped) and plain paragraphs. No files are written to disk and no
    external module is needed - a .docx is a zip of three small XML parts plus a styles part.
    Dot-source it; it defines functions only and performs no work on load.
#>

Set-StrictMode -Version Latest

$script:WordNamespace = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'

function ConvertTo-WordXmlText {
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)

    [System.Security.SecurityElement]::Escape($Text)
}

function ConvertTo-WordParagraph {
    [CmdletBinding()]
    [OutputType([string])]
    param(
        [Parameter(Mandatory)][string]$Text,
        [string]$Style,
        [switch]$Bold,
        [switch]$Bullet
    )

    $properties = ''
    if ($Style) { $properties += "<w:pStyle w:val=`"$Style`"/>" }
    if ($Bullet) { $properties += '<w:ind w:left="360" w:hanging="260"/>' }
    $properties = if ($properties) { "<w:pPr>$properties</w:pPr>" } else { '' }

    $runProperties = if ($Bold) { '<w:rPr><w:b/></w:rPr>' } else { '' }
    $content = if ($Bullet) { "$([char]0x2022) $Text" } else { $Text }
    "<w:p>$properties<w:r>$runProperties<w:t xml:space=`"preserve`">$(ConvertTo-WordXmlText $content)</w:t></w:r></w:p>"
}

function ConvertTo-WordTable {
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][string[]]$Rows)

    $xml = [System.Text.StringBuilder]::new()
    [void]$xml.Append('<w:tbl><w:tblPr><w:tblStyle w:val="TableGrid"/><w:tblW w:w="0" w:type="auto"/></w:tblPr>')
    $first = $true
    foreach ($row in $Rows) {
        $cells = @($row.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() })
        [void]$xml.Append('<w:tr>')
        foreach ($cell in $cells) {
            $paragraph = ConvertTo-WordParagraph -Text $cell -Bold:$first
            [void]$xml.Append("<w:tc><w:tcPr><w:tcW w:w=`"0`" w:type=`"auto`"/></w:tcPr>$paragraph</w:tc>")
        }
        [void]$xml.Append('</w:tr>')
        $first = $false
    }
    [void]$xml.Append('</w:tbl>')
    # Word requires a paragraph after a table at the end of a cell/body; the caller appends one for spacing.
    $xml.ToString()
}

function ConvertTo-WordBodyXml {
    [CmdletBinding()]
    [OutputType([string])]
    param([Parameter(Mandatory)][AllowEmptyString()][string[]]$Lines)

    $body = [System.Text.StringBuilder]::new()
    $tableRows = [System.Collections.Generic.List[string]]::new()

    $flushTable = {
        if ($tableRows.Count -gt 0) {
            [void]$body.Append((ConvertTo-WordTable -Rows $tableRows.ToArray()))
            [void]$body.Append('<w:p/>')
            $tableRows.Clear()
        }
    }

    foreach ($line in $Lines) {
        if ($line -match '^\s*\|') {
            if ($line -notmatch '^\s*\|[\s:|-]+\|\s*$') { $tableRows.Add($line) }
            continue
        }
        & $flushTable

        switch -Regex ($line) {
            '^##\s+(.+)$' { [void]$body.Append((ConvertTo-WordParagraph -Text $Matches[1] -Style 'Heading2')); break }
            '^#\s+(.+)$' { [void]$body.Append((ConvertTo-WordParagraph -Text $Matches[1] -Style 'Heading1')); break }
            '^-\s+(.+)$' { [void]$body.Append((ConvertTo-WordParagraph -Text $Matches[1] -Bullet)); break }
            '^\s*$' { break }
            default { [void]$body.Append((ConvertTo-WordParagraph -Text $line.Trim())) }
        }
    }
    & $flushTable
    $body.ToString()
}

function ConvertTo-DocxByteArray {
    <#
    .SYNOPSIS
        Builds a .docx from Markdown-subset lines and returns its bytes.
    .PARAMETER Lines
        The Markdown source, one element per line.
    #>
    [CmdletBinding()]
    [OutputType([byte[]])]
    param([Parameter(Mandatory)][AllowEmptyString()][string[]]$Lines)

    $ns = $script:WordNamespace
    $parts = [ordered]@{
        '[Content_Types].xml'          = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/></Types>'
        '_rels/.rels'                  = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>'
        'word/_rels/document.xml.rels' = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>'
        'word/styles.xml'              = "<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?><w:styles xmlns:w=`"$ns`"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=`"Calibri`" w:hAnsi=`"Calibri`"/><w:sz w:val=`"22`"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after=`"120`"/></w:pPr></w:pPrDefault></w:docDefaults><w:style w:type=`"paragraph`" w:default=`"1`" w:styleId=`"Normal`"><w:name w:val=`"Normal`"/></w:style><w:style w:type=`"paragraph`" w:styleId=`"Heading1`"><w:name w:val=`"heading 1`"/><w:basedOn w:val=`"Normal`"/><w:next w:val=`"Normal`"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before=`"240`" w:after=`"120`"/><w:outlineLvl w:val=`"0`"/></w:pPr><w:rPr><w:b/><w:sz w:val=`"32`"/></w:rPr></w:style><w:style w:type=`"paragraph`" w:styleId=`"Heading2`"><w:name w:val=`"heading 2`"/><w:basedOn w:val=`"Normal`"/><w:next w:val=`"Normal`"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before=`"200`" w:after=`"80`"/><w:outlineLvl w:val=`"1`"/></w:pPr><w:rPr><w:b/><w:sz w:val=`"26`"/></w:rPr></w:style><w:style w:type=`"table`" w:styleId=`"TableGrid`"><w:name w:val=`"Table Grid`"/><w:tblPr><w:tblBorders><w:top w:val=`"single`" w:sz=`"4`" w:space=`"0`" w:color=`"808080`"/><w:left w:val=`"single`" w:sz=`"4`" w:space=`"0`" w:color=`"808080`"/><w:bottom w:val=`"single`" w:sz=`"4`" w:space=`"0`" w:color=`"808080`"/><w:right w:val=`"single`" w:sz=`"4`" w:space=`"0`" w:color=`"808080`"/><w:insideH w:val=`"single`" w:sz=`"4`" w:space=`"0`" w:color=`"808080`"/><w:insideV w:val=`"single`" w:sz=`"4`" w:space=`"0`" w:color=`"808080`"/></w:tblBorders></w:tblPr></w:style></w:styles>"
        'word/document.xml'            = "<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?><w:document xmlns:w=`"$ns`"><w:body>$(ConvertTo-WordBodyXml -Lines $Lines)</w:body></w:document>"
    }

    $stream = [System.IO.MemoryStream]::new()
    try {
        $archive = [System.IO.Compression.ZipArchive]::new($stream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($name in $parts.Keys) {
                $entry = $archive.CreateEntry($name)
                $entryStream = $entry.Open()
                try {
                    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes([string]$parts[$name])
                    $entryStream.Write($bytes, 0, $bytes.Length)
                }
                finally {
                    $entryStream.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
        return , $stream.ToArray()
    }
    finally {
        $stream.Dispose()
    }
}
