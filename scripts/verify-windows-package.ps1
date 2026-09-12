[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string]$Directory,
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [ValidateSet('Test', 'Prerelease', 'Stable')] [string]$PackageKind
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $Directory).Path
$channel = switch ($PackageKind) { 'Test' { 'test' }; 'Prerelease' { 'beta' }; 'Stable' { 'win' } }
$prefix = "FinalCheck-v$Version-win-x64"
$checksum = Join-Path $root "$prefix-SHA256.txt"
$expected = @("$prefix-Setup.exe", "$prefix-Portable.zip", "$prefix-package-metrics.json", "$prefix-SHA256.txt", "releases.$channel.json", "assets.$channel.json")
foreach ($name in $expected) {
    $path = Join-Path $root $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -le 0) { throw "Missing or empty package asset: $name" }
}

$hashedFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($line in Get-Content -LiteralPath $checksum) {
    if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw "Invalid SHA256 entry." }
    $hash = $Matches[1]
    $relative = $Matches[2]
    $path = [IO.Path]::GetFullPath($relative, $root)
    if (-not $path.StartsWith($root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Checksum path escapes artifact directory." }
    if (-not $hashedFiles.Add($path)) { throw "Duplicate checksum entry: $relative" }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw "Checksum mismatch: $relative" }
}
$allFiles = @(Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object FullName -ne $checksum)
if ($allFiles.Count -ne $hashedFiles.Count -or @($allFiles | Where-Object { -not $hashedFiles.Contains($_.FullName) }).Count -gt 0) { throw "SHA256 manifest must cover every output asset exactly once." }

$feed = Get-Content -LiteralPath (Join-Path $root "releases.$channel.json") -Raw | ConvertFrom-Json
$fullAssets = @($feed.Assets | Where-Object Type -eq 'Full')
if ($fullAssets.Count -ne 1) { throw "Expected one full update package." }
$full = $fullAssets[0]
if ($full.Version -ne $Version -or $full.PackageId -ne 'FinalCheck.App') { throw "Update metadata version/ID mismatch." }
$nupkgPath = Join-Path $root $full.FileName
if ((Get-Item -LiteralPath $nupkgPath).Length -ne $full.Size -or (Get-FileHash -LiteralPath $nupkgPath -Algorithm SHA256).Hash -ne $full.SHA256) { throw "Update metadata hash/size mismatch." }
foreach ($asset in @(Get-Content -LiteralPath (Join-Path $root "assets.$channel.json") -Raw | ConvertFrom-Json)) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $asset.RelativeFileName) -PathType Leaf)) { throw "Upload metadata references a missing asset: $($asset.RelativeFileName)" }
}

$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $root "$prefix-Portable.zip"))
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("finalcheck-package-verify-" + [guid]::NewGuid().ToString('N'))
try {
    if (@($archive.Entries | Where-Object { $_.FullName -match '(?i)(\.db(?:-wal|-shm)?$|\.sqlite3?$|/\.env(?:\.|$))' }).Count -gt 0) { throw "Portable includes private runtime data." }
    $entry = $archive.GetEntry('current/sq.version')
    if (-not $entry) { throw "Portable lacks Velopack sq.version." }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $metadata = $manifest.SelectSingleNode('//*[local-name()="metadata"]')
    if ($metadata.version -ne $Version -or $metadata.id -ne 'FinalCheck.App' -or $metadata.channel -ne $channel) { throw "Portable package version/ID/channel mismatch." }
    New-Item -ItemType Directory -Path $tempRoot | Out-Null
    $assemblyEntry = $archive.GetEntry('current/FinalCheck.App.dll')
    if (-not $assemblyEntry) { throw "Portable lacks About display assembly." }
    $assemblyPath = Join-Path $tempRoot 'FinalCheck.App.dll'
    [IO.Compression.ZipFileExtensions]::ExtractToFile($assemblyEntry, $assemblyPath)
    $displayVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath).ProductVersion
    if ($displayVersion -ne $Version) { throw "About assembly version mismatch: $displayVersion" }
}
finally {
    $archive.Dispose()
    if (Test-Path -LiteralPath $tempRoot) {
        $resolvedTemp = (Resolve-Path -LiteralPath $tempRoot).Path
        $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
        if (-not $resolvedTemp.StartsWith($tempBase + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Unexpected verification directory." }
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}
Write-Output "Package verification passed: $Version ($channel)."
