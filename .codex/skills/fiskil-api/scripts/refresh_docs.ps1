param(
    [string]$BaseUrl = "https://docs.fiskil.com",
    [string]$SkillRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$referencesRoot = Join-Path $SkillRoot "references"
$manifestPath = Join-Path $referencesRoot "manifest.md"
$llmsUrl = "$BaseUrl/llms.txt"

New-Item -ItemType Directory -Force -Path $referencesRoot | Out-Null

function Get-DataApiLinks {
    param([string]$Content)

    $inDataApi = $false
    $links = [System.Collections.Generic.List[object]]::new()

    foreach ($line in ($Content -split "`r?`n")) {
        if ($line -eq "## Data Api") {
            $inDataApi = $true
            continue
        }

        if ($inDataApi -and $line.StartsWith("## ") -and $line -ne "## Data Api") {
            break
        }

        if (-not $inDataApi) {
            continue
        }

        if ($line -match '^- \[(?<title>[^\]]+)\]\((?<path>/data-api/(guides|api-reference|changelog)[^)]+)\)(: (?<description>.*))?$') {
            $links.Add([pscustomobject]@{
                Title = $Matches.title
                Path = $Matches.path
                Description = if ($Matches.description) { $Matches.description } else { "" }
            })
        }
    }

    $links | Sort-Object Path -Unique
}

function ConvertTo-LocalPath {
    param([string]$DocPath)

    $relativePath = $DocPath.TrimStart("/") + ".mdx"
    Join-Path $referencesRoot ($relativePath -replace "/", [System.IO.Path]::DirectorySeparatorChar)
}

$llms = (Invoke-WebRequest -UseBasicParsing $llmsUrl).Content
$links = @(Get-DataApiLinks -Content $llms)

if ($links.Count -eq 0) {
    throw "No Data API links found in $llmsUrl"
}

$manifest = [System.Collections.Generic.List[string]]::new()
$manifest.Add("# Fiskil Data API Documentation Manifest")
$manifest.Add("")
$generatedAt = Get-Date -Format "yyyy-MM-dd HH:mm:ss zzz"
$manifest.Add("Generated from $llmsUrl on $generatedAt.")
$manifest.Add("")
$manifest.Add("Downloaded pages: $($links.Count)")
$manifest.Add("")

$downloaded = 0
$failed = [System.Collections.Generic.List[string]]::new()

foreach ($link in $links) {
    $sourceUrl = "$BaseUrl$($link.Path).mdx"
    $destination = ConvertTo-LocalPath -DocPath $link.Path
    $destinationDirectory = Split-Path -Parent $destination

    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null

    try {
        $content = (Invoke-WebRequest -UseBasicParsing $sourceUrl).Content
        Set-Content -LiteralPath $destination -Value $content -Encoding UTF8
        $downloaded++

        $relativeDestination = $destination.Substring($referencesRoot.Length).TrimStart("\", "/") -replace "\\", "/"
        $manifest.Add(("- [{0}]({1}) - ``{2}``" -f $link.Title, $relativeDestination, $link.Path))
    }
    catch {
        $failed.Add(("{0}: {1}" -f $link.Path, $_.Exception.Message))
    }
}

if ($failed.Count -gt 0) {
    $manifest.Add("")
    $manifest.Add("## Failed Downloads")
    foreach ($failure in $failed) {
        $manifest.Add("- $failure")
    }
}

Set-Content -LiteralPath $manifestPath -Value $manifest -Encoding UTF8

Write-Output "Downloaded $downloaded of $($links.Count) Fiskil Data API pages."
Write-Output "Manifest: $manifestPath"

if ($failed.Count -gt 0) {
    Write-Output "Failures: $($failed.Count)"
    exit 1
}
