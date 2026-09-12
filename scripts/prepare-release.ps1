[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [ValidateSet('stable', 'prerelease')] [string]$Channel,
    [Parameter(Mandatory)] [AllowEmptyString()] [string]$ReleaseNotes,
    [AllowEmptyString()] [string]$ReadmeSummary = '',
    [string]$ConfirmRealRelease = 'false',
    [string]$AllowReleaseWithin24h = 'false',
    [Parameter(Mandatory)] [string]$DefaultBranch,
    [Parameter(Mandatory)] [string]$RefName,
    [Parameter(Mandatory)] [string]$BaseSha,
    [Parameter(Mandatory)] [string]$OutputDirectory,
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseSafety.psm1') -Force
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$props = Join-Path $root 'Directory.Build.props'

if ($ConfirmRealRelease -ne 'true') { throw '未勾选真实发布确认。测试安装包请使用 Build Windows test installer。' }
if ($RefName -ne $DefaultBranch) { throw "Release 只能从默认分支 $DefaultBranch 运行。" }
if ($Channel -eq 'stable' -and $Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') { throw 'Stable version must use x.y.z.' }
if ($Channel -eq 'prerelease' -and ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*$' -or $Version -match '-dev\.')) { throw 'Prerelease requires a non-internal SemVer suffix, e.g. x.y.z-beta.1.' }
if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) { throw 'Release notes must not be empty.' }
if ($Channel -eq 'stable' -and [string]::IsNullOrWhiteSpace($ReadmeSummary)) { throw 'Stable Release requires a README summary.' }
if (@(Invoke-CheckedGit $root @('status', '--porcelain')).Count -gt 0) { throw 'Release preparation requires a clean working tree.' }

& (Join-Path $PSScriptRoot 'version.ps1') check -PropsPath $props | Write-Output
& (Join-Path $PSScriptRoot 'version.ps1') assert-not-lower $Version -PropsPath $props | Write-Output
$currentVersion = & (Join-Path $PSScriptRoot 'version.ps1') print -PropsPath $props
$tag = "v$Version"
Assert-ReleaseTagAbsent $root $tag
$null = Invoke-CheckedGit $root @('fetch', '--tags', 'origin')
Assert-ReleaseTagAbsent $root $tag
Assert-ReleaseBranchUnchanged $root $DefaultBranch $BaseSha

$tagLines = @(Invoke-CheckedGit $root @('for-each-ref', '--sort=-creatordate', '--format=%(refname:short)|%(creatordate:unix)', 'refs/tags'))
$latest = $tagLines | Where-Object { $_ -match '^v\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?\|' } | Select-Object -First 1
if ($latest) {
    $parts = $latest -split '\|', 2
    $latestTime = [DateTimeOffset]::FromUnixTimeSeconds([int64]$parts[1])
    $hours = ([DateTimeOffset]::UtcNow - $latestTime).TotalHours
    if ($hours -lt 24 -and $AllowReleaseWithin24h -ne 'true') { throw "距上一发布 Tag $($parts[0]) 不足 24 小时。确认必要时才可勾选 24 小时内发布。" }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$notesPath = [IO.Path]::GetFullPath((Join-Path $OutputDirectory 'release-notes.md'))
$summaryPath = [IO.Path]::GetFullPath((Join-Path $OutputDirectory 'readme-summary.md'))
[IO.File]::WriteAllText($notesPath, "## 版本亮点`n`n$($ReleaseNotes.Trim())`n", [Text.UTF8Encoding]::new($false))
if ($Channel -eq 'stable') { [IO.File]::WriteAllText($summaryPath, $ReadmeSummary.Trim(), [Text.UTF8Encoding]::new($false)) }
$packageKind = if ($Channel -eq 'stable') { 'Stable' } else { 'Prerelease' }
$outputs = [ordered]@{version=$Version; tag=$tag; package_kind=$packageKind; notes_path=$notesPath; readme_summary_path=$summaryPath; base_sha=$BaseSha; current_version=$currentVersion}
if ($env:GITHUB_OUTPUT) {
    foreach ($entry in $outputs.GetEnumerator()) { "$($entry.Key)=$($entry.Value)" >> $env:GITHUB_OUTPUT }
}
$outputs | ConvertTo-Json | Write-Output
# Successful 'Tag absent' queries return git's code 2; do not leak it to Actions.
exit 0
