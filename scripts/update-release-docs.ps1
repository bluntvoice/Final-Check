[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [ValidateSet('stable', 'prerelease')] [string]$Channel,
    [Parameter(Mandatory)] [string]$NotesPath,
    [string]$ReadmeSummaryPath,
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$sourceVersion = & (Join-Path $PSScriptRoot 'version.ps1') print -PropsPath (Join-Path $root 'Directory.Build.props')
if ($sourceVersion -ne $Version) { throw 'Release document version must equal the source version.' }
if ($Channel -eq 'stable' -and $Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Stable version must use x.y.z.' }
if ($Channel -eq 'prerelease' -and ($Version -notmatch '^\d+\.\d+\.\d+-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*$' -or $Version -match '-dev\.')) { throw 'Prerelease version must have a non-internal suffix.' }
$notes = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $NotesPath)).Trim()
if ($notes -notmatch '(?m)^## 版本亮点[ \t]*\r?$') { throw 'Release notes require the heading ## 版本亮点.' }
$changelogPath = Join-Path $root 'CHANGELOG.md'
$changelog = [IO.File]::ReadAllText($changelogPath)
$anchor = '<!-- release-changelog:entries -->'
if ([regex]::Matches($changelog, [regex]::Escape($anchor)).Count -ne 1) { throw 'CHANGELOG anchor missing or duplicated.' }
if ($changelog -match "(?m)^## \[v$([regex]::Escape($Version))\](?:\s|$)") { throw "CHANGELOG already contains v$Version." }
$entryNotes = [regex]::Replace($notes, '(?m)^## 版本亮点[ \t]*\r?$', '### 版本亮点', 1)
$updatedChangelog = $changelog.Replace($anchor, "$anchor`n`n## [v$Version] - $([DateTimeOffset]::UtcNow.ToString('yyyy-MM-dd'))`n`n$entryNotes")

$readmePath = Join-Path $root 'README.md'
$updatedReadme = $null
if ($Channel -eq 'stable') {
    if (-not $ReadmeSummaryPath) { throw 'Stable Release requires a summary file.' }
    $summary = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $ReadmeSummaryPath)).Trim()
    if ([string]::IsNullOrWhiteSpace($summary)) { throw 'README summary must not be empty.' }
    $updatedReadme = [IO.File]::ReadAllText($readmePath)
    $replacements = [ordered]@{
        'current-version' = "当前正式版本：**[v$Version](https://github.com/bluntvoice/Final-Check/releases/tag/v$Version)**。"
        'summary' = $summary
    }
    foreach ($replacement in $replacements.GetEnumerator()) {
        $start = "<!-- release-readme:$($replacement.Key):start -->"
        $end = "<!-- release-readme:$($replacement.Key):end -->"
        if ([regex]::Matches($updatedReadme, [regex]::Escape($start)).Count -ne 1 -or [regex]::Matches($updatedReadme, [regex]::Escape($end)).Count -ne 1) { throw "README markers missing/duplicated: $($replacement.Key)" }
        $startIndex = $updatedReadme.IndexOf($start) + $start.Length
        $endIndex = $updatedReadme.IndexOf($end)
        if ($endIndex -lt $startIndex) { throw 'README marker order invalid.' }
        $updatedReadme = $updatedReadme.Substring(0, $startIndex) + "`n$($replacement.Value)`n" + $updatedReadme.Substring($endIndex)
    }
}

# Validate all input/anchors first; only then write the allowlisted documents.
[IO.File]::WriteAllText($changelogPath, $updatedChangelog, [Text.UTF8Encoding]::new($false))
if ($Channel -eq 'stable') { [IO.File]::WriteAllText($readmePath, $updatedReadme, [Text.UTF8Encoding]::new($false)) }
Write-Output "Release documents updated: v$Version ($Channel)."
